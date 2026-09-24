using Sirenix.OdinInspector;
using TakoBoyStudios.Animation;
using TakoBoyStudios.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TakoBoyStudios.TopDown2D
{
    public enum PlayerState
    {
        Jump = EntityState.Custom,
        Damaged,
        Throwing,
        Reviving,
    }

    /// <summary>
    /// Shared base for every playable character. Everything that is the same no matter who you are
    /// playing lives here: input, movement, the combat jump, the basic gun, camera bounds, health and
    /// the between-rooms reset. What differs per character is the look of the body, so the two view
    /// hooks (<see cref="UpdateCharacterAnimation"/> for the grounded pose and <see cref="PlayJumpVisual"/>
    /// for the leap) are left empty here and filled in by the concrete characters.
    ///
    /// Concrete characters derive from this: <c>Milky</c> drives the two-part (top and bottom) split
    /// animation, <c>Grim</c> drives a single-body three-facing set, and <c>Sen</c> will slot in the same
    /// way later. This class is never placed on a prefab directly.
    /// </summary>
    public class Player : Character
    {
        #region Components

        [BoxGroup("Player Weapons")]
        [SerializeField]
        GunComponent m_basicGun;

        [BoxGroup("Jump")]
        [Tooltip("Seconds the jump is in the air. Data-driven, not tied to the animation length. ~0.55.")]
        [SerializeField, MinValue(0.05f)]
        float jumpFlightTime = 0.55f;

        [BoxGroup("Jump")]
        [Tooltip("Apex height of the jump, in pixels (fake-Z). Grim's is 32.")]
        [SerializeField, MinValue(0f)]
        float jumpPeakHeight = 12f;

        [BoxGroup("Jump")]
        [Tooltip(
            "Where the top of the head is drawn, in pixels above the player's position, standing. What a "
            + "jump bumps into things above it with, such as a Perk bubble. Grim's art tops out around 10."
        )]
        [SerializeField, MinValue(0f)]
        float headHeight = 10f;

        /// <summary>The top of the head above the player's position while standing, in pixels. Add Z for the head in the air.</summary>
        public float HeadHeight => headHeight;

        [BoxGroup("Jump")]
        [Tooltip("Top horizontal speed while steering in the air, in u/s. ~100 (normal move speed): the jump is evasion, not a burst dash.")]
        [SerializeField, MinValue(0f)]
        float jumpMoveSpeed = 100f;

        [BoxGroup("Jump")]
        [Tooltip(
            "How quickly the stick changes the jump's horizontal speed, in u/s per second. The jump leaves the "
            + "ground with none, so this is also how fast it gets going. ~600 reaches full speed in about a "
            + "sixth of a second; higher is snappier, lower drifts more."
        )]
        [SerializeField, MinValue(0f)]
        float airAcceleration = 600f;

        // The walk model (T-460). Shape taken from the reference player's walk: a push-off
        // acceleration that reaches full speed in ten frames whatever the top speed, a brake that
        // stops in three, a direction that bends toward the stick and snaps once nearly aligned,
        // and a reversal that plants (brakes to a stop) before going the other way. The numbers are
        // fractions of the top speed so a speed tweak keeps the feel.
        [BoxGroup("Walk")]
        [Tooltip(
            "How much of the top walk speed is added per second while the stick is held. With the "
                + "growth below, 4.3 reaches full speed in about ten frames from a standstill whatever "
                + "the top speed is. Higher is snappier."
        )]
        [SerializeField, MinValue(0f)]
        float walkAcceleration = 4.31f;

        [BoxGroup("Walk")]
        [Tooltip(
            "Proportional growth of the current walk speed per second while accelerating: the speed "
                + "grows by this fraction of itself each second on top of the flat acceleration. 5 makes "
                + "the start read as a push off rather than a straight ramp. 0 for a plain linear ramp."
        )]
        [SerializeField, MinValue(0f)]
        float walkGrowth = 5f;

        [BoxGroup("Walk")]
        [Tooltip(
            "How much of the top walk speed is shed per second when the stick is released or pushed "
                + "back against the walk. 18 stops from full speed in about three frames."
        )]
        [SerializeField, MinValue(0f)]
        float walkBrake = 18.1f;

        [BoxGroup("Walk")]
        [Tooltip(
            "How fast the walk direction bends toward a new stick direction, per second, doubling as "
                + "they come into line. 16 turns a right angle in about five frames, and the direction "
                + "snaps to the stick once within about eleven degrees. A reversal (more than a right "
                + "angle away) does not bend: it brakes to a stop, then goes the new way."
        )]
        [SerializeField, MinValue(0f)]
        float walkTurnRate = 16f;

        [BoxGroup("Walk")]
        [Tooltip(
            "Off, the design pillar: movement is free angle and the stick's magnitude scales the speed. "
                + "On: the stick is snapped to eight directions, and a stick within about 22 degrees of "
                + "an axis walks straight along it. Here to be tried against the reference, not the default."
        )]
        [SerializeField]
        bool eightWayMovement;

        [BoxGroup("Walk")]
        [Tooltip(
            "How long a diagonal keeps facing and aiming the diagonal after one of its two keys lifts, in "
                + "seconds. On a keyboard or d-pad the two keys of a diagonal never lift on the same frame, "
                + "so letting go always passes through a frame or two of whichever key lifted last. Inside "
                + "this window that straggler is never shown or fired along, so a diagonal idle is "
                + "reachable; past it, the single key is a real turn and is taken. A turn from a diagonal "
                + "to one of its own cardinals is late by at most this much. Sticks are never held back. "
                + "About 0.08."
        )]
        [SerializeField, MinValue(0f)]
        float releaseGrace = 0.08f;

        [BoxGroup("Walk")]
        [Tooltip(
            "The aim only. How long the first key of an aim waits for a second before the gun fires, in "
                + "seconds. The two keys of a diagonal never go down on the same frame either, so without "
                + "this pressing up and right fires one bullet straight up before the diagonal. A second "
                + "key inside the window starts the aim on the diagonal; otherwise it starts on the one "
                + "key. Every straight shot from rest on keys is late by this much, so keep it short. "
                + "Sticks never wait. About 0.05."
        )]
        [SerializeField, MinValue(0f)]
        float pressGrace = 0.05f;

        /// <summary>Past this dot with the current walk direction the stick is a turn; below it, a reversal.</summary>
        const float ReversalDot = -0.01f;

        /// <summary>Within this dot of the stick the bending direction snaps to it exactly.</summary>
        const float TurnSnapDot = 0.98f;

        /// <summary>Eight-way only: a normalised stick component at or above this walks straight along that axis.</summary>
        const float CardinalBand = 0.925f;

        // The walk in flight: where it is going and how fast, in pixels per second. Fed to the motor
        // through SetMoveDirection every frame the player is walking.
        Vector2 _walkDirection = Vector2.down;
        float _walkSpeed;
        bool _skidPlayed;

        // Release grace (T-468). When one key of a diagonal lifted and the diagonal started being held
        // in place of the straggler, for the stick and for the aim separately. Negative when nothing
        // is being held back.
        float _facingLiftAt = -1f;
        float _aimLiftAt = -1f;

        // Press grace (T-468). When the first key of an aim went down and it started waiting for a
        // second, and the way that first key points. Negative when nothing is waiting.
        float _aimPressAt = -1f;
        Vector2 _aimPressed;

        /// <summary>
        /// On the frame the aim is released, the aim it was released from, which the release grace
        /// keeps on the diagonal when a pair of keys let go one after the other. Zero on every other
        /// frame. A character reads it to face its idle the way the player was really pointing.
        /// </summary>
        protected Vector2 _aimReleasedFacing;

        /// <summary>The walk's current speed along <see cref="WalkDirection"/>, in pixels per second.</summary>
        public float WalkSpeed => _walkSpeed;

        /// <summary>The direction the walk is currently carrying the player, unit length.</summary>
        public Vector2 WalkDirection => _walkDirection;

        [BoxGroup("Jump")]
        [Tooltip("How long a jump press is remembered so a press just before landing fires on touchdown. ~0.10s.")]
        [SerializeField, MinValue(0f)]
        float jumpBuffer = 0.10f;

        [BoxGroup("Throw")]
        [Tooltip("What gets thrown. Leave empty on a character who cannot throw and the input does nothing.")]
        [SerializeField]
        Bomb bombPrefab;

        [BoxGroup("Throw")]
        [Tooltip(
            "How far along the aim it lands, in pixels. A bomb goes to a spot rather than off in a "
                + "direction, so this is the whole of its range. The encounter frame is 256 wide, so "
                + "about 90 lands it comfortably inside a fight without reaching across one."
        )]
        [SerializeField, MinValue(0f)]
        float throwDistance = 90f;

        [BoxGroup("Throw")]
        [Tooltip(
            "Which frame of the throw animation the bomb leaves the hand on. The clip opens with a "
                + "240ms wind up and ends in a follow through, so 2 is the moment the arm comes "
                + "through. Read off the animation rather than timed beside it, so what you see and "
                + "what the game does cannot drift apart."
        )]
        [SerializeField, MinValue(0)]
        int throwReleaseFrame = 2;

        [BoxGroup("Throw")]
        [Tooltip("How many bombs can be in the air at once. Fixed and pre-warmed, so a throw never allocates.")]
        [SerializeField, MinValue(1)]
        int bombPoolSize = 8;

        [BoxGroup("Throw")]
        [Tooltip("How many bombs the player starts with. Each throw spends one; at zero the throw does nothing until they are refilled. The player begins a run with 3.")]
        [SerializeField, MinValue(0)]
        int startingBombs = 3;

        [BoxGroup("Hurt")]
        [Tooltip("Phase 1, the frozen reaction: seconds with motion locked, input ignored and the damaged clip playing. Cannot be hit. ~0.35s.")]
        [SerializeField, MinValue(0.05f)]
        float damagedDuration = 0.35f;

        [BoxGroup("Hurt")]
        [Tooltip("Phase 2, the recovery: seconds of free-moving invincibility after the freeze. The sprite blinks, hits still pass through, and the player can walk out of whatever hit them. ~0.8s.")]
        [SerializeField, MinValue(0f)]
        float invulnDuration = 0.8f;

        [BoxGroup("Hurt")]
        [Tooltip("How fast the sprite flickers on and off during the invincible recovery, in seconds per toggle. ~0.08s.")]
        [SerializeField, MinValue(0.02f)]
        float blinkInterval = 0.08f;

        [BoxGroup("Revive")]
        [Tooltip("How close a partner has to stand to a downed player's ghost to revive them with Interact, in pixels. A tile and a half is 24.")]
        [SerializeField, MinValue(0f)]
        float reviveRange = 24f;

        [BoxGroup("Revive")]
        [Tooltip("How long the reviver kneels, rooted, before the ghost comes back, in seconds. A hit cancels it. ~1.2.")]
        [SerializeField, MinValue(0f)]
        float reviveDuration = 1.2f;

        [BoxGroup("Revive")]
        [Tooltip("Seconds of blinking invincibility a revived player gets, so they are not killed again the moment they stand. ~1.5.")]
        [SerializeField, MinValue(0f)]
        float reviveInvulnDuration = 1.5f;

        // Downed and revive (T-438). A dead player is downed: the death clip plays out, then they wait as
        // a ghost until something calls Resurrect. The engine's Dead state is the downed state, so
        // everything that already treats a dead player as out of play (doors, the camera, LivingCount)
        // treats a downed one the same way with no change.
        bool _deathPlayed;
        bool _resurrectRequested;
        bool _resurrecting;
        Player _reviveTarget;
        float _reviveTimer;

        PlayerInput _playerInput;
        InputAction _moveAction;
        InputAction _shootAction;
        InputAction _jumpAction;
        InputAction _bombAction;

        Vector2 _throwDirection = Vector2.down;

        /// <summary>Whether the throw clip has been seen at frame 0, so its frames may be counted. See StateThrowing.</summary>
        bool _throwStarted;
        bool _thrown;

        int _bombsRemaining;

        /// <summary>Bombs left to throw. The HUD's grip slot counts down off this.</summary>
        public int BombsRemaining => _bombsRemaining;

        /// <summary>
        /// While set, no input reaches the player: no movement, aim, jump or bomb. Held from death
        /// through the game-over screen and the tavern arrival until Sal has spoken (T-369). The
        /// dead state already reads nothing; this covers the revived Grim standing in the tavern.
        /// </summary>
        public bool InputLocked { get; set; }

        InputAction _submitAction;
        InputAction _navigateAction;
        InputAction _interactAction;

        /// <summary>
        /// The gameplay map's Interact, what a player presses to take the thing they are standing at.
        ///
        /// **Deliberately on the gameplay map and not UI/Submit.** Reaching for Submit would mean
        /// pushing the UI map, which turns the gameplay map off entirely, and the whole point of an
        /// in-world choice is that the player keeps walking, aiming and dodging while they make it.
        ///
        /// Optional, the same as Bomb: an older input asset without the action leaves this null and
        /// anything asking for it simply never fires.
        /// </summary>
        public InputAction InteractAction
        {
            get
            {
                if (_interactAction == null && _playerInput != null)
                    _interactAction = _playerInput.actions.FindAction("Interact");
                return _interactAction;
            }
        }

        /// <summary>True on the frame Interact went down, and false whenever input is locked.</summary>
        public bool InteractPressed => !InputLocked && InteractAction != null && InteractAction.WasPressedThisFrame();

        /// <summary>The UI map's Submit, what a screen advances on. Null if the asset lacks it.</summary>
        public InputAction SubmitAction
        {
            get
            {
                if (_submitAction == null && _playerInput != null)
                    _submitAction = _playerInput.actions.FindAction("UI/Submit");
                return _submitAction;
            }
        }

        /// <summary>The UI map's Navigate, what moves a highlight through choices. Null if the asset lacks it.</summary>
        public InputAction NavigateAction
        {
            get
            {
                if (_navigateAction == null && _playerInput != null)
                    _navigateAction = _playerInput.actions.FindAction("UI/Navigate");
                return _navigateAction;
            }
        }

        // Which action map is live, as a stack, so a screen can take the controls and hand them back
        // without knowing what was underneath. Only one map is enabled at a time: while the UI map is
        // on top the gameplay map is off entirely, so nothing can leak into a jump or a shot, and a
        // press that ends the screen is not seen again by the map that comes back (T-370).
        readonly System.Collections.Generic.List<string> _inputMapStack = new System.Collections.Generic.List<string>(4);

        /// <summary>Switch to another action map, remembering the current one. Pop to go back.</summary>
        public void PushInputMap(string map)
        {
            if (_playerInput == null || _playerInput.currentActionMap == null)
                return;
            _inputMapStack.Add(_playerInput.currentActionMap.name);
            _playerInput.SwitchCurrentActionMap(map);
        }

        /// <summary>Return to the map that was live before the last push. Safe when nothing was pushed.</summary>
        public void PopInputMap()
        {
            if (_playerInput == null || _inputMapStack.Count == 0)
                return;
            string back = _inputMapStack[_inputMapStack.Count - 1];
            _inputMapStack.RemoveAt(_inputMapStack.Count - 1);
            _playerInput.SwitchCurrentActionMap(back);
        }

        /// <summary>The name of the action map currently live, for debugging.</summary>
        public string CurrentInputMap => _playerInput != null && _playerInput.currentActionMap != null ? _playerInput.currentActionMap.name : "";

        /// <summary>The renderer drawing Grim's body right now, for a screen that needs his current frame.</summary>
        public SpriteRenderer BodySprite
        {
            get
            {
                if (_bodyRenderers == null)
                    return null;
                for (int i = 0; i < _bodyRenderers.Length; i++)
                    if (_bodyRenderers[i] != null && _bodyRenderers[i].sprite != null)
                        return _bodyRenderers[i];
                return null;
            }
        }

        /// <summary>Show or hide the body sprites, shadow untouched. The game-over screen hides him once its own Grim takes over.</summary>
        public void SetBodyVisible(bool visible) => ShowBody(visible);

        /// <summary>
        /// Stand still facing a direction, for a scripted moment: waking at the tavern faces the bar
        /// (T-371). Characters with their own idle facing override this to turn their pose too.
        /// </summary>
        /// <summary>
        /// Which character this is, as the game's own data names them: "grim", "sen".
        ///
        /// The package has no roster and never will, so the base answer is deliberately useless and
        /// each character class says who it is. What reads it is anything keyed per character: which
        /// Perks may be offered, which dialogue lines are theirs, which save slot a run belongs to.
        /// </summary>
        public virtual string CharacterId => "player";

        /// <summary>
        /// Give up the seat when the body goes. A destroyed player already reads as an empty seat
        /// through Unity's null comparison, but leaving explicitly is what raises
        /// <see cref="Players.Left"/> so a HUD block can hide itself rather than sitting on a corpse.
        /// </summary>
        void OnDestroy() => Players.Leave(this);

        public virtual void Face(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;
            m_lastMoveDirection = direction.normalized;
            ResetWalk();
            SetMoveDirection(Vector2.zero);
            m_moveInput = Vector2.zero;
        }

        /// <summary>
        /// Drops the walk to a standstill. Anything that takes the stick away from the walk (a jump, a
        /// hit, a throw, a room change) calls this, so the next step starts from rest the way the
        /// first one did rather than at whatever speed the interruption caught it.
        /// </summary>
        protected void ResetWalk()
        {
            _walkSpeed = 0f;
            _skidPlayed = false;
        }

        /// <summary>The clip the character put on for this throw, so its frames are read and nothing else's.</summary>
        string _throwClip;

        protected Vector2 _shootDirection;

        // Last frame's snapped aim, fed back in so a direction held near a sector boundary does not
        // flicker between two octants. Cleared the moment the player stops aiming.
        Vector2 _heldAim;
        Vector2 _jumpDirection;
        float _jumpBufferTimer;
        bool _airborne;
        float _damagedTimer;

        // Invincibility spans both hurt phases (the freeze and the free-moving blink), so it is tracked
        // separately from the FSM state, which only owns the freeze.
        float _invulnTimer;
        float _blinkTimer;
        bool _blinkVisible = true;
        SpriteRenderer[] _bodyRenderers;

        #endregion

        public override void Init()
        {
            base.Init();

            // Setup new input system
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null)
                _playerInput = gameObject.AddComponent<PlayerInput>();

            _moveAction = _playerInput.actions["Move"];
            _shootAction = _playerInput.actions["Shoot"];
            _jumpAction = _playerInput.actions["Jump"];

            // Optional so an older input asset without the action does not throw on load; the throw
            // simply does nothing until the binding exists.
            _bombAction = _playerInput.actions.FindAction("Bomb");

            // Nothing else creates this one. Enemy projectiles get their pools from the wave runner
            // that spawns them, and a bomb builds its own explosion pool, but the player's own bomb has
            // no such owner: without this, Acquire returns null and the throw silently does nothing.
            if (bombPrefab != null && PoolManager.Instance != null)
            {
                PoolManager.Instance.CreatePool(
                    bombPrefab.name,
                    bombPrefab.gameObject,
                    new PoolConfig(bombPoolSize, bombPoolSize, grow: 0, autoGrow: false)
                );
            }

            _bombsRemaining = startingBombs;

            if (m_basicGun)
                m_basicGun.Init(this);

            // The body sprites (not the shadow, which hangs off the root) are what the recovery blink
            // flickers. Cache them once from the visual root.
            _bodyRenderers = character != null
                ? character.GetComponentsInChildren<SpriteRenderer>(true)
                : System.Array.Empty<SpriteRenderer>();

            // Same pattern as the camera line above: the player announces itself and whoever
            // cares binds. The HUD reads health off this reference (T-326, T-336).
            // Take a seat before announcing, so anything reacting to the spawn can already ask which
            // player this is and how many there are.
            Players.Join(this);

            // Only players are held inside the shared view. Set here rather than on the prefab so the
            // layer stays an implementation detail of whoever raises the frame.
            if (motor != null)
                motor.ContainedByPlayerFrame = true;

            // The facing is decided here, from the stick, with the release grace below. Left to the
            // physics tick it would be overwritten with the bending walk direction during the brake.
            m_ownsLastMoveDirection = true;

            EntityEvents.ReportPlayerSpawned(this);
        }

        public override void AddStates()
        {
            base.AddStates();
            m_fsm.AddState(StateJump);
            m_fsm.AddState(StateDamaged);
            m_fsm.AddState(StateThrowing);
            m_fsm.AddState(StateReviving);
        }

        /// <summary>
        /// Drops the player back to a clean grounded Idle at a new position, for the endless lab between
        /// rooms and after a death. Reactivates the object if a death deactivated it, cancels any jump in
        /// progress, forces height back to the floor, restores full health, and stops all motion.
        /// Permanent setup (input, gun, camera target) is left alone.
        /// </summary>
        public void ResetForRoom(Vector2 worldPosition) => PlaceInRoom(worldPosition, restoreHealth: true);

        /// <summary>
        /// Moves the player into a room they have walked into, keeping the state a run is made of.
        ///
        /// Health carries across a door. Restoring it would make every transition a free heal and
        /// take the pressure out of deciding whether to push on, which is most of what a run is.
        /// Everything else is still reset: a jump in progress, invincibility, knockback and any
        /// buffered input, none of which should survive a change of room.
        /// </summary>
        public void MoveToRoom(Vector2 worldPosition) => PlaceInRoom(worldPosition, restoreHealth: false);

        void PlaceInRoom(Vector2 worldPosition, bool restoreHealth)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            _shootDirection = Vector2.zero;
            _heldAim = Vector2.zero;
            _aimPressAt = -1f;
            _jumpBufferTimer = 0f;
            _airborne = false;
            ResetWalk();

            // A walk-in from the room being left must not carry over: it would march the player off
            // their new spot the moment they are placed (seen waking at the tavern, T-372). The new
            // room starts its own walk-in after this if it has one.
            CancelWalkIn();

            // Land cleanly even if the reset happened mid-jump, then reset vitals and drop to Idle.
            if (motor != null)
                motor.ResetVertical();

            // Clear any in-progress hurt invincibility so the fresh room starts solid and hittable.
            EndInvulnerability();

            // Standing up from any route (a revive, the tavern wake) ends being downed, and a revive
            // this player was giving does not carry into the new room.
            _deathPlayed = false;
            _resurrectRequested = false;
            _resurrecting = false;
            _reviveTarget = null;

            if (restoreHealth)
            {
                OnAcquired(); // restores hp, clears hit lag and impulse, returns the FSM to Idle
            }
            else
            {
                // The same reset OnAcquired does, minus the health. Hit lag and knockback are
                // per-hit state and must not ride through a door; the FSM has to come back to Idle
                // or a player who left mid-jump arrives still jumping.
                StopHitLag();
                m_impulseVelocity = Vector2.zero;
                // Immediate, for the same reason as Entity.OnAcquired: a queued change leaves the
                // player reading as whatever they were for the rest of the frame.
                m_fsm?.ForceState((int)EntityState.Idle);
            }

            SetMoveDirection(Vector2.zero);
            m_moveInput = Vector2.zero;
            m_lastMoveDirection = Vector2.down;
            Position = worldPosition;
        }

        // -----------------------------
        // Tick Loop
        // -----------------------------
        protected override bool Tick(float deltaTime)
        {
            // Sample the jump buffer before the FSM runs, so a press made this frame (or held over from
            // the air) is visible to StateIdle when it decides whether to launch.
            PollJumpBuffer(deltaTime);

            // Invincibility and its blink run independently of the FSM so they carry past the freeze into
            // the free-moving recovery.
            UpdateInvulnerability(deltaTime);

            if (!base.Tick(deltaTime))
                return false;

            if (m_basicGun)
                m_basicGun.Tick(deltaTime);

            return true;
        }

        void PollJumpBuffer(float deltaTime)
        {
            if (InputLocked)
            {
                _jumpBufferTimer = 0f;
                return;
            }

            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _jumpBufferTimer = jumpBuffer;
            else if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= deltaTime;
        }

        // Containment used to be a position clamp against the active CamLock's screen rect. It is now
        // physical: a CamLock raises CombatBoundary colliders flush with the visible edges and the
        // motor sweeps against them like any wall, so knockback, the jump arc and sliding along the
        // edge all behave the way they do against real geometry.

        // -----------------------------
        // FSM States (Logic Only)
        // -----------------------------
        /// <summary>True once the death clip has played out. What the game-over beat waits on, since a ghost that follows never finishes.</summary>
        public bool DeathPlayed => _deathPlayed;

        /// <summary>A resurrection has been asked for or is playing. Counts as standing for anything deciding whether the run is over.</summary>
        public bool IsResurrecting => _resurrectRequested || _resurrecting;

        /// <summary>Down and waiting for a revive: dead, and nothing is bringing them back yet.</summary>
        public bool IsDowned => IsDead && !IsResurrecting;

        /// <summary>
        /// Brings a downed player back where they fell: the death clip finishes if it is still playing,
        /// the resurrection plays, and they stand up at full health with a short blinking invincibility.
        /// Safe to call on the frame of death, before the Dead state has been applied. Does nothing to
        /// a player who is alive.
        /// </summary>
        public void Resurrect()
        {
            if (Health > 0 && !IsDead)
                return;

            _resurrectRequested = true;
        }

        /// <summary>
        /// Grim dying, and then downed (T-438). The base entity disposes itself the moment it enters
        /// this state, which is right for a slime and catastrophic for the player: it would pool Grim
        /// away mid-run and leave the room being played by nobody. So this deliberately does not call
        /// base.
        ///
        /// He stops, loses control, and plays the death out. Then one of three things: a resurrection
        /// that has been asked for plays; with a partner still standing he waits as a ghost for them to
        /// revive him; alone, he stays on the last frame of the death, and the room decides the run is
        /// over. The ghost is chosen every frame, so it appears the moment a partner stands back up.
        /// </summary>
        protected override void StateDead(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    m_moveInput = Vector2.zero;
                    m_impulseVelocity = Vector2.zero;
                    SetMoveDirection(Vector2.zero);
                    _deathPlayed = false;
                    _resurrecting = false;

                    // Hit lag pauses the animator and is cleared inside Tick, which returns early for
                    // anything dead. Every other entity disposes on the frame it dies so never notices;
                    // Grim stays, and dying from a hit is the ordinary way to die, so without this his
                    // death clip is frozen on frame 0 by the pause that killed him.
                    StopHitLag();

                    // A body on the floor is not a target: shots pass over it rather than stopping on
                    // it. Ended first so a hurt blink still running cannot switch it back on mid-death.
                    EndInvulnerability();
                    SetInvulnerable(true);

                    // Straight to the clip, not queued. The FSM only applies a queued change on its
                    // next tick, and the player is not ticked while dead, so a queued animation would
                    // never arrive. Same trap as T-230.
                    PlayDownedClip(AnimConst.Death);
                    break;

                case Fsm.StateStep.Update:
                    // No input is read and the gun is not ticked.
                    if (_resurrecting)
                    {
                        if (m_entityAnimator == null
                            || m_entityAnimator.CurrentAnimationName != AnimConst.Resurrection
                            || m_entityAnimator.IsDone)
                        {
                            FinishResurrection();
                        }
                        break;
                    }

                    if (!_deathPlayed)
                    {
                        if (m_entityAnimator != null
                            && m_entityAnimator.CurrentAnimationName == AnimConst.Death
                            && !m_entityAnimator.IsDone)
                        {
                            break;
                        }
                        _deathPlayed = true;
                    }

                    if (_resurrectRequested)
                    {
                        _resurrectRequested = false;
                        _resurrecting = true;
                        if (!PlayDownedClip(AnimConst.Resurrection))
                            FinishResurrection();
                        break;
                    }

                    // Only worth waiting as a ghost while somebody could revive. LivingCount does not
                    // count this player, who is dead by now.
                    if (Players.LivingCount > 0 && m_entityAnimator != null
                        && m_entityAnimator.CurrentAnimationName != AnimConst.Ghost)
                    {
                        PlayDownedClip(AnimConst.Ghost);
                    }

                    if (DebugDraw.Enabled)
                        DebugDraw.Box(Position, new Vector2(reviveRange * 2f, reviveRange * 2f), new Color(0.4f, 1f, 0.7f, 1f));
                    break;
            }
        }

        /// <summary>
        /// Puts on one of the downed clips from its first frame. Clears a pause first, because Grim's
        /// idle pose freezes the animator and Play does not resume it. False when the clip is missing.
        /// </summary>
        bool PlayDownedClip(string clip)
        {
            if (m_entityAnimator == null || !m_entityAnimator.HasAnimation(clip))
                return false;

            m_entityAnimator.Paused = false;
            m_entityAnimator.Play(clip);
            return true;
        }

        void FinishResurrection()
        {
            // Back to a clean, full-health Idle where they lie. PlaceInRoom clears the downed flags
            // and ends the invulnerability, so the revive's own window is started after it.
            PlaceInRoom(Position, restoreHealth: true);

            if (reviveInvulnDuration > 0f)
            {
                SetInvulnerable(true);
                _invulnTimer = reviveInvulnDuration;
                _blinkTimer = 0f;
                _blinkVisible = true;
            }
        }

        /// <summary>
        /// Starts reviving a downed partner in reach, on Interact. The nearest one wins. Walks the seats
        /// by index, so a press allocates nothing.
        /// </summary>
        bool TryStartRevive()
        {
            if (!InteractPressed)
                return false;

            Player best = null;
            float bestDistance = reviveRange;
            int seats = Players.SeatCount;
            for (int i = 0; i < seats; i++)
            {
                Player other = Players.At(i);
                if (other == null || other == this || !other.IsDowned)
                    continue;

                float distance = Vector2.Distance(other.Position, Position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = other;
                }
            }

            if (best == null)
                return false;

            _reviveTarget = best;
            m_fsm.ChangeState((int)PlayerState.Reviving);
            return true;
        }

        /// <summary>
        /// Kneeling over a downed partner (T-438). Rooted and not shooting for
        /// <see cref="reviveDuration"/>, then the partner resurrects. Committing is the cost: a hit
        /// moves this player to Damaged, which ends the revive, and the partner stays down.
        /// </summary>
        void StateReviving(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    ResetWalk();
                    SetMoveDirection(Vector2.zero);
                    m_moveInput = Vector2.zero;
                    _shootDirection = Vector2.zero;
                    _reviveTimer = 0f;
                    PlayDownedClip(AnimConst.Revive);
                    break;

                case Fsm.StateStep.Update:
                    SetMoveDirection(Vector2.zero);

                    // Someone else got there first, or the partner left the game.
                    if (_reviveTarget == null || !_reviveTarget.IsDowned)
                    {
                        _reviveTarget = null;
                        m_fsm.ChangeState((int)EntityState.Idle);
                        break;
                    }

                    if (DebugDraw.Enabled)
                        DebugDraw.Line(Position, _reviveTarget.Position, new Color(0.4f, 1f, 0.7f, 1f));

                    _reviveTimer += deltaTime;
                    if (_reviveTimer >= reviveDuration)
                    {
                        _reviveTarget.Resurrect();
                        _reviveTarget = null;
                        m_fsm.ChangeState((int)EntityState.Idle);
                    }
                    break;

                case Fsm.StateStep.Exit:
                    _reviveTarget = null;
                    break;
            }
        }

        protected override void StateIdle(Fsm.StateStep step, float deltaTime)
        {
            base.StateIdle(step, deltaTime);

            if (step != Fsm.StateStep.Update)
                return;

            // Walking in through a door is the one time the player is not driving. Input is ignored
            // rather than the component being disabled, so animation, physics and everything else keep
            // running and Grim walks in looking like himself.
            if (TickScriptedWalk(deltaTime))
                return;

            if (TryStartRevive())
                return;

            HandleMovementInput(deltaTime);
            HandleAttackInput();
            HandleJumpInput();
            HandleThrowInput();
        }

        #region Scripted walk

        Vector2 _scriptedDirection;
        float _scriptedRemaining;

        /// <summary>True while the player is being walked rather than played.</summary>
        public bool IsWalkingIn => _scriptedRemaining > 0f;

        /// <summary>
        /// Takes the controls away and walks the player a fixed distance in one direction.
        ///
        /// This is what makes arriving somewhere feel like arriving: Grim steps in through the door
        /// under his own power, and only then does the room become the player's problem. It is a
        /// distance rather than a duration so it ends where it is supposed to end, whatever the frame
        /// rate did, and it is cancelled by anything that takes the player out of Idle.
        /// </summary>
        public void BeginWalkIn(Vector2 direction, float distance)
        {
            if (direction.sqrMagnitude < 0.0001f || distance <= 0f)
                return;

            _scriptedDirection = direction.normalized;
            _scriptedRemaining = distance;
        }

        public void CancelWalkIn()
        {
            _scriptedRemaining = 0f;
            m_moveInput = Vector2.zero;
        }

        /// <summary>Drives the walk. Returns true while it owns the player.</summary>
        bool TickScriptedWalk(float deltaTime)
        {
            if (_scriptedRemaining <= 0f)
                return false;

            _scriptedRemaining -= m_moveSpeed * deltaTime;

            if (_scriptedRemaining <= 0f)
            {
                CancelWalkIn();
                WalkInFinished?.Invoke();
                return false;
            }

            SetMoveDirection(_scriptedDirection);
            m_lastMoveDirection = _scriptedDirection;
            return true;
        }

        /// <summary>Raised when a walk-in finishes, which is when the door behind can close.</summary>
        public event System.Action WalkInFinished;

        #endregion

        /// <summary>
        /// Starts a throw on the press, aimed where the player is aiming.
        ///
        /// Aim, not movement, on purpose: the throw commits to where you are pointing, so a player can
        /// back away from a fight and still put a bomb into it. Falls back to the last facing when nothing is held, so a
        /// standing player throws where they are looking rather than nowhere.
        /// </summary>
        void HandleThrowInput()
        {
            if (InputLocked || _bombAction == null || bombPrefab == null || !_bombAction.WasPressedThisFrame())
                return;

            // Out of bombs: no throw and no wind-up, so an empty grip reads as empty rather than
            // miming a throw that produces nothing.
            if (_bombsRemaining <= 0)
                return;

            // The same answer the body already uses for which way it is facing: aim first, then
            // movement, then the direction last faced. Reimplementing it here was the bug: the old
            // fallback was a field that started as Vector2.down, so a player standing still and not
            // aiming always threw south, and the bomb went into the wall below them.
            //
            // Snapped to eight, like the gun. A throw that could go anywhere would be the only thing
            // in the game that does.
            _throwDirection = Aim.Snap(GetAnimationFacingDirection());

            if (_throwDirection.sqrMagnitude < 0.0001f)
                _throwDirection = Vector2.down;
            m_fsm.ChangeState((int)PlayerState.Throwing);
        }

        /// <summary>
        /// The combat jump. **It leaves the ground straight up and the player steers it in the air**
        /// (owner, 2026-09-14): horizontal speed starts at zero and follows the move stick, eased by
        /// <see cref="airAcceleration"/> up to <see cref="jumpMoveSpeed"/>. The height arc is the
        /// motor's, and shooting is off for the whole airtime. Defense comes purely from the
        /// height/attack relationship, not i-frames: the player is never SetInvincible here.
        ///
        /// It used to lock the stick's direction and speed on takeoff. That read as being thrown
        /// somewhere rather than jumping.
        /// </summary>
        void StateJump(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    // Only which way the leap pose faces. Nothing about where the body goes.
                    _jumpDirection =
                        m_moveInput.sqrMagnitude > 0.001f ? m_moveInput.normalized : m_lastMoveDirection;
                    if (_jumpDirection.sqrMagnitude < 0.001f)
                        _jumpDirection = Vector2.down;

                    _jumpBufferTimer = 0f;
                    _airborne = false;
                    _airVelocity = Vector2.zero;
                    ResetWalk();
                    SetMoveDirection(Vector2.zero);

                    if (motor != null)
                        motor.LaunchArc(jumpPeakHeight, jumpFlightTime);

                    // The leap pose is character-specific; the airtime is the motor's arc, not the clip.
                    PlayJumpVisual(_jumpDirection);
                    break;

                case Fsm.StateStep.Update:
                    SteerInAir(deltaTime);

                    if (!Grounded)
                        _airborne = true;
                    else if (_airborne)
                        m_fsm.ChangeState((int)EntityState.Idle);
                    break;

                case Fsm.StateStep.Exit:
                    _airVelocity = Vector2.zero;
                    SetMoveDirection(Vector2.zero);
                    break;
            }
        }

        /// <summary>Horizontal velocity while airborne, in u/s. Eased toward the stick every frame.</summary>
        Vector2 _airVelocity;

        void SteerInAir(float deltaTime)
        {
            Vector2 input = InputLocked || _moveAction == null ? Vector2.zero : _moveAction.ReadValue<Vector2>();
            if (input.sqrMagnitude > 1f)
                input.Normalize();

            _airVelocity = Vector2.MoveTowards(_airVelocity, input * jumpMoveSpeed, airAcceleration * deltaTime);

            // Entity applies m_moveInput * m_moveSpeed, so divide it back out to get the velocity asked for.
            float scale = m_moveSpeed > 0.0001f ? 1f / m_moveSpeed : 0f;
            SetMoveDirection(_airVelocity * scale);
        }

        // -----------------------------
        // Damage / Hurt
        // -----------------------------
        /// <summary>
        /// A hit that lands (the hitbox was hittable) applies its damage and, if it did not kill, starts
        /// the hurt reaction: the hitbox goes untouchable for the whole invincibility window and the FSM
        /// drops into the frozen Phase 1. Once invincibility is running no further hits get through, so
        /// the guard is really only belt and braces.
        /// </summary>
        /// <summary>
        /// Who dealt the last hit that actually took health. Read at death to say what killed the
        /// player (T-328). The source Entity of the hit, whatever it was: an enemy, a projectile, an
        /// explosion. Null until the first real hit lands.
        /// </summary>
        public Entity LastDamageSource { get; private set; }

        /// <summary>
        /// Damage arrives in hits (an ordinary one is DamageValues.HitDamage) and the player's health is
        /// counted in pips of DamageValues.PlayerHealthPerPip, so it is converted: 10 takes 4, 20 takes 8,
        /// and a Burn tick of 2 takes 1. Anything that does damage takes at least 1, so a small status
        /// tick is never free.
        /// </summary>
        protected override int ScaleIncomingDamage(int damage)
        {
            if (damage <= 0)
                return 0;

            return Mathf.Max(1, Mathf.RoundToInt(damage * (float)DamageValues.PlayerHealthPerPip / DamageValues.HitDamage));
        }

        public override void DealDamage(HitEvent hitEvent)
        {
            if (_invulnTimer > 0f || (m_fsm != null && m_fsm.CurrentState == (int)PlayerState.Damaged))
                return;

            int healthBefore = Health;

            // Recorded before the base call: a fatal hit calls Die() inside it, which raises OnDeath
            // synchronously, and the death handler reads this to name the killer (T-328). Set it after
            // and the record is always one hit stale, or null on a one-hit kill.
            LastDamageSource = hitEvent.damageInfo.source;

            base.DealDamage(hitEvent); // applies hp, raises OnTakeDamage, may Die

            // Report only health actually lost (T-326). The early return above means an i-frame
            // rejection never reaches this line, and the base call not raising OnTakeDamage on a
            // fatal hit is exactly why the comparison lives here: the killing blow must break the
            // chain like any other hit, or dying would be the one hit that does not.
            if (Health < healthBefore)
                EntityEvents.ReportPlayerDamaged(this);

            // Health, not IsDead. Die() only *queues* the change to Dead, so IsDead still reads false
            // on the line after the fatal hit, and the hurt reaction below would replace the queued
            // death with a flinch. The player would take the killing blow and simply carry on.
            // Health is set to zero synchronously inside Die(), so it is the honest signal here.
            //
            // Third time this queue has bitten: T-230 cleared a whole wave early on it, and T-228
            // wrote the same warning into Entity.SetCombatState.
            if (Health > 0 && !IsDead && m_fsm != null)
            {
                // The hurt reaction is our own freeze; the generic hit-lag would otherwise pause the
                // animator and stall the tick, delaying the transition and the damaged pose. Drop it.
                StopHitLag();

                // Untouchable for the freeze plus the blinking recovery that follows it.
                SetInvulnerable(true);
                _invulnTimer = damagedDuration + invulnDuration;
                _blinkTimer = 0f;
                _blinkVisible = true;

                m_fsm.ChangeState((int)PlayerState.Damaged);
            }
        }

        /// <summary>
        /// Phase 1 of the hurt reaction, the frozen part. For <see cref="damagedDuration"/> seconds the
        /// player cannot act: steering and knockback are zeroed so a moving player stops dead, any shot is
        /// dropped, and a hit taken mid-air freezes in place with gravity off (Exit clears the hover so he
        /// resumes his fall). When it ends the FSM returns to Idle, where Phase 2 plays out: the player
        /// moves freely while still invincible and blinking, driven by <see cref="UpdateInvulnerability"/>,
        /// until the invincibility window expires. The damaged clip is character-specific, hence the hook.
        /// </summary>
        void StateDamaged(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    _damagedTimer = damagedDuration;

                    // Cannot act: forget any buffered jump and stop shooting this frame.
                    _shootDirection = Vector2.zero;
                    _heldAim = Vector2.zero;
                    _aimPressAt = -1f;
                    _jumpBufferTimer = 0f;
                    _airborne = false;

                    // Cannot move: kill steering and the incoming knockback so the hit does not slide us.
                    ResetWalk();
                    SetMoveDirection(Vector2.zero);
                    m_moveInput = Vector2.zero;
                    m_impulseVelocity = Vector2.zero;
                    StopHitLag(); // unpause so the damaged clip actually plays

                    // Frozen mid-air: if hit off the ground, pin the current height with gravity off so
                    // the player just hangs there with the hurt pose. Exit clears the hover and gravity
                    // takes back over, so he drops from where he was rather than snapping to the floor.
                    if (motor != null && !Grounded)
                        motor.SetHover(motor.Height);

                    PlayDamagedVisual();
                    break;

                case Fsm.StateStep.Update:
                    // Hold perfectly still for the whole freeze.
                    SetMoveDirection(Vector2.zero);

                    _damagedTimer -= deltaTime;
                    if (_damagedTimer <= 0f)
                        m_fsm.ChangeState((int)EntityState.Idle);
                    break;

                case Fsm.StateStep.Exit:
                    // Release any mid-air freeze so gravity resumes and the fall continues into Phase 2. A
                    // no-op if the hit was taken on the ground. Invincibility keeps running past here.
                    if (motor != null)
                        motor.ClearHover();
                    break;
            }
        }

        /// <summary>
        /// Runs the invincibility window every frame, independent of the FSM so it spans both hurt phases.
        /// During the frozen Phase 1 the body stays solid on the damaged pose; once the freeze ends the
        /// body flickers on and off so the free-moving player reads as invincible. When the timer runs out
        /// the hitbox becomes hittable again and the body is restored.
        /// </summary>
        void UpdateInvulnerability(float deltaTime)
        {
            if (_invulnTimer <= 0f)
                return;

            _invulnTimer -= deltaTime;
            if (_invulnTimer <= 0f)
            {
                EndInvulnerability();
                return;
            }

            // No blink during the frozen phase: the damaged clip should read cleanly.
            if (m_fsm != null && m_fsm.CurrentState == (int)PlayerState.Damaged)
            {
                ShowBody(true);
                return;
            }

            _blinkTimer -= deltaTime;
            if (_blinkTimer <= 0f)
            {
                _blinkTimer = blinkInterval;
                _blinkVisible = !_blinkVisible;
                ShowBody(_blinkVisible);
            }
        }

        void EndInvulnerability()
        {
            _invulnTimer = 0f;
            _blinkTimer = 0f;
            _blinkVisible = true;
            ShowBody(true);
            SetInvulnerable(false);
        }

        /// <summary>
        /// Turns the player's hitbox on or off as a target: invincible blocks damage and phased lets
        /// attacks pass straight through rather than stopping on the body.
        /// </summary>
        void SetInvulnerable(bool invulnerable)
        {
            if (m_hitboxes == null)
                return;

            for (int i = 0; i < m_hitboxes.Count; i++)
            {
                if (m_hitboxes[i] == null)
                    continue;

                m_hitboxes[i].SetInvincible(invulnerable);
                m_hitboxes[i].SetPhased(invulnerable);
            }
        }

        void ShowBody(bool visible)
        {
            if (_bodyRenderers == null)
                return;

            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null && _bodyRenderers[i].enabled != visible)
                    _bodyRenderers[i].enabled = visible;
            }
        }

        /// <summary>
        /// Play the character's hurt pose. Empty here so each character supplies its own damaged art;
        /// the freeze, invincibility and blink are handled by the base regardless.
        /// </summary>
        protected virtual void PlayDamagedVisual() { }

        // -----------------------------
        // Input Handling
        // -----------------------------
        void HandleMovementInput(float deltaTime)
        {
            Vector2 input = InputLocked ? Vector2.zero : _moveAction.ReadValue<Vector2>();
            if (input.sqrMagnitude > 1f)
                input.Normalize();

            if (eightWayMovement)
                input = SnapWalkToEight(input);

            // Facing follows the stick the frame it moves, whatever the walk itself is doing, except
            // while the release grace is holding a diagonal whose keys are letting go (T-468). The walk
            // below still gets the real input either way: only the facing waits.
            if (input.sqrMagnitude > 0.001f)
            {
                if (!HoldForRelease(Aim.Snap(m_lastMoveDirection), Aim.Snap(input), _moveAction, ref _facingLiftAt))
                    m_lastMoveDirection = input;
            }
            else
            {
                _facingLiftAt = -1f;
            }

            if (DebugDraw.Enabled)
            {
                // The facing the body is drawn with: white, or amber while the grace is holding it.
                Color facingColour = _facingLiftAt >= 0f ? new Color(1f, 0.7f, 0.2f, 1f) : Color.white;
                DebugDraw.Line(Position, (Vector2)Position + m_lastMoveDirection.normalized * 12f, facingColour);
            }

            StepWalk(input, deltaTime);
        }

        /// <summary>
        /// One frame of the walk (T-460). The stick is a request; the walk has its own speed and
        /// direction and moves them toward it: a push-off acceleration up to the top speed, a bend
        /// toward a new direction that snaps once nearly aligned, and a brake to a stop when the
        /// stick is released or reversed. A reversal only turns once the brake has reached zero, so a
        /// hard about-face reads as a plant rather than a teleport. The result is handed to the motor
        /// as a move input scaled so that Entity's <c>input * speed</c> comes out at the walk's speed.
        /// </summary>
        void StepWalk(Vector2 input, float deltaTime)
        {
            float magnitude = input.magnitude;
            bool held = magnitude > 0.001f;

            // What the run and the statuses make of the base speed (Chill, a speed Perk), scaled by
            // the stick when movement is free angle. Eight-way input arrives at unit length.
            float baseSpeed = GetMoveSpeed();
            float top = baseSpeed * Mathf.Min(1f, magnitude);
            Vector2 wanted = held ? input / magnitude : Vector2.zero;

            // From a standstill there is nothing to bend: the first step goes where the stick points.
            if (held && _walkSpeed <= 0.0001f)
                _walkDirection = wanted;

            float dot = held ? Vector2.Dot(wanted, _walkDirection) : -1f;

            if (held && dot >= ReversalDot)
            {
                _skidPlayed = false;

                if (dot < TurnSnapDot)
                {
                    float rate = walkTurnRate * (1f + Mathf.Abs(dot));
                    _walkDirection += (wanted - _walkDirection) * Mathf.Min(1f, rate * deltaTime);
                    if (_walkDirection.sqrMagnitude > 0.0001f)
                        _walkDirection.Normalize();
                    else
                        _walkDirection = wanted;
                }
                else
                {
                    _walkDirection = wanted;
                }

                _walkSpeed = _walkSpeed * (1f + walkGrowth * deltaTime) + walkAcceleration * top * deltaTime;
                if (_walkSpeed > top)
                    _walkSpeed = top;
            }
            else
            {
                // Released, or pushed back against the walk: brake. The reversal is the one time the
                // body visibly fights the stick, so it gets a pose if the character has one.
                if (held && _walkSpeed > 0f && !_skidPlayed)
                {
                    _skidPlayed = true;
                    PlaySkidVisual(_walkDirection);
                }

                _walkSpeed -= walkBrake * baseSpeed * deltaTime;
                if (_walkSpeed <= 0f)
                {
                    _walkSpeed = 0f;
                    if (held)
                        _walkDirection = wanted;
                }
            }

            // Entity applies m_moveInput * m_moveSpeed, so divide the walk's speed back out.
            float scale = m_moveSpeed > 0.0001f ? 1f / m_moveSpeed : 0f;
            SetMoveDirection(_walkDirection * (_walkSpeed * scale));

            if (DebugDraw.Enabled && _walkSpeed > 0f)
            {
                // Where the walk is carrying him and how fast: a quarter second of travel.
                DebugDraw.Line(Position, (Vector2)Position + _walkDirection * (_walkSpeed * 0.25f), new Color(0.3f, 0.9f, 1f, 1f));
            }
        }

        /// <summary>
        /// Eight-way input with a cardinal band: a stick within about 22 degrees of an axis walks
        /// straight along it, anything else walks the diagonal at unit length, so a diagonal is the
        /// same speed as a cardinal.
        /// </summary>
        static Vector2 SnapWalkToEight(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
                return Vector2.zero;

            Vector2 n = input.normalized;
            if (Mathf.Abs(n.x) >= CardinalBand)
                return new Vector2(Mathf.Sign(n.x), 0f);
            if (Mathf.Abs(n.y) >= CardinalBand)
                return new Vector2(0f, Mathf.Sign(n.y));
            return new Vector2(Mathf.Sign(n.x), Mathf.Sign(n.y)) * 0.70710678f;
        }

        /// <summary>
        /// The reversal pose, played once when the stick is pushed back against a walk in progress and
        /// the brake begins. Empty here; a character with skid art faces it the way the walk was going.
        /// </summary>
        protected virtual void PlaySkidVisual(Vector2 direction) { }

        /// <summary>
        /// The release grace (T-468). True while <paramref name="next"/> should be held back and
        /// <paramref name="held"/> kept, both snapped to eight: <paramref name="held"/> is a diagonal
        /// on keys or buttons, <paramref name="next"/> is one of its own two cardinals, which is what
        /// a diagonal looks like with one key lifted, and that has lasted no longer than
        /// <see cref="releaseGrace"/>.
        ///
        /// The two keys of a diagonal never lift on the same frame, so letting go passes through a
        /// frame or two of the straggler's cardinal. The first fix put the diagonal back once both
        /// were up, which still drew the cardinal in between: the flicker. Holding it back instead
        /// means a release inside the window never sees the cardinal at all, and a real turn is taken
        /// once the window has passed. A stick is never held: released, it springs back through the
        /// middle along the way it was pointing and does not pass a cardinal.
        ///
        /// <paramref name="liftAt"/> is the caller's own timer, negative when nothing is held.
        /// </summary>
        bool HoldForRelease(Vector2 held, Vector2 next, InputAction action, ref float liftAt)
        {
            bool heldDiagonal = Mathf.Abs(held.x) > 0.1f && Mathf.Abs(held.y) > 0.1f;
            bool keyLift = heldDiagonal
                && next != held
                && Vector2.Dot(held, next) > 0.5f
                && action.activeControl is ButtonControl;

            if (!keyLift)
            {
                liftAt = -1f;
                return false;
            }

            if (liftAt < 0f)
                liftAt = Time.time;

            if (Time.time - liftAt < releaseGrace)
                return true;

            liftAt = -1f;
            return false;
        }

        /// <summary>
        /// The press grace (T-468), the aim only. True while the start of an aim should wait: nothing
        /// was being aimed, <paramref name="snapped"/> is a cardinal on keys or buttons, and it has
        /// waited no longer than <see cref="pressGrace"/>. A second key inside that makes a diagonal,
        /// which ends the wait and starts the aim there; past it, the one key starts the aim on its
        /// own. Remembers the cardinal so a tap released mid-wait can still fire it.
        ///
        /// The walk has no press grace. A press skew only costs it one frame of facing, and a walk
        /// that set off upward still facing whatever it faced before would look worse than that.
        /// </summary>
        bool HoldForPress(Vector2 snapped)
        {
            bool cardinal = Mathf.Abs(snapped.x) < 0.1f || Mathf.Abs(snapped.y) < 0.1f;
            bool starting = _heldAim.sqrMagnitude < 0.0001f;

            if (!starting || !cardinal || !(_shootAction.activeControl is ButtonControl))
            {
                _aimPressAt = -1f;
                return false;
            }

            if (_aimPressAt < 0f)
                _aimPressAt = Time.time;

            _aimPressed = snapped;

            if (Time.time - _aimPressAt < pressGrace)
                return true;

            _aimPressAt = -1f;
            return false;
        }

        /// <summary>
        /// Reads the aim and locks it to one of eight directions.
        ///
        /// The snap happens here, at the input, rather than inside the gun. Aiming is a property of
        /// how the player is played, not of what they are holding, so every weapon inherits it and
        /// the animation, the muzzle and the bullet cannot disagree about which way Grim is pointing.
        ///
        /// The dead zone matters as much as the snap: without it a resting thumb on a drifting stick
        /// fires continuously in whatever direction the drift happens to favour.
        /// </summary>
        void HandleAttackInput()
        {
            _shootDirection = Vector2.zero;
            _aimReleasedFacing = Vector2.zero;

            if (InputLocked)
            {
                _heldAim = Vector2.zero;
                _aimLiftAt = -1f;
                _aimPressAt = -1f;
                return;
            }

            Vector2 shootInput = _shootAction.ReadValue<Vector2>();

            if (!Aim.IsAiming(shootInput))
            {
                // The window check drops a wait left over from before a jump or a throw, since this
                // only runs while standing and nothing else clears it.
                if (_aimPressAt >= 0f && Time.time - _aimPressAt <= pressGrace)
                {
                    // A tap let go while it was still waiting for a second key. It never got to fire,
                    // so it fires now, once, the way it pointed; next frame is its release.
                    _aimPressAt = -1f;
                    _heldAim = _aimPressed;
                }
                else
                {
                    // Released: face the aim it was released from. If a pair of keys let go one after
                    // the other, the release grace has kept that on the diagonal the whole time.
                    _aimReleasedFacing = _heldAim;
                    _heldAim = Vector2.zero;
                    _aimLiftAt = -1f;
                    _aimPressAt = -1f;
                    return;
                }
            }
            else
            {
                Vector2 snapped = Aim.Snap(shootInput, _heldAim);

                // The key skew on the way down: the first key of a diagonal lands a frame or two
                // before the second, and the gun fires on the first frame of an aim. Waiting briefly
                // for the second is what stops a lone bullet going straight up before the diagonal.
                if (HoldForPress(snapped))
                {
                    if (DebugDraw.Enabled)
                        DebugDraw.Line(Position, (Vector2)Position + snapped * 20f, new Color(1f, 0.7f, 0.2f, 1f));
                    return;
                }

                // The same key-lift skew as the walk, and here it costs more than a flicker: the torso
                // would turn for a frame and the gun could fire a stray shot along the straggler's
                // cardinal. Held back, the shot keeps going the way the pair pointed.
                if (!HoldForRelease(_heldAim, snapped, _shootAction, ref _aimLiftAt))
                    _heldAim = snapped;
            }

            _shootDirection = _heldAim;

            if (DebugDraw.Enabled)
            {
                // The aim being fired along: red, or amber while the release grace is holding it.
                Color aimColour = _aimLiftAt >= 0f ? new Color(1f, 0.7f, 0.2f, 1f) : new Color(1f, 0.3f, 0.3f, 1f);
                DebugDraw.Line(Position, (Vector2)Position + _heldAim * 20f, aimColour);
            }

            if (m_basicGun)
                m_basicGun.Shoot(_shootDirection);

            Attack(_shootDirection);
        }

        void HandleJumpInput()
        {
            // Only from grounded Idle (this runs in StateIdle). The buffer lets a press made in the air
            // fire the instant the player lands.
            if (_jumpBufferTimer > 0f && Grounded)
                m_fsm.ChangeState((int)PlayerState.Jump);
        }

        // -----------------------------
        // Animations (View Only)
        // -----------------------------
        protected override void UpdateAnimations(float deltaTime)
        {
            if (!m_entityAnimator)
            {
                base.UpdateAnimations(deltaTime);
                return;
            }

            // The jump, the hurt reaction and the death each drive their own clip; leave them alone.
            //
            // Death matters most here. The FSM applies its queued change and runs Enter, and then this
            // runs later in the same tick, so without the guard the death clip is replaced by an idle
            // on the very frame it starts. It is chosen by the state rather than by what the body is
            // doing, which is exactly why it cannot be left to a function that reads the body.
            if (m_fsm.CurrentState == (int)PlayerState.Jump
                || m_fsm.CurrentState == (int)PlayerState.Damaged
                || m_fsm.CurrentState == (int)PlayerState.Throwing
                || m_fsm.CurrentState == (int)PlayerState.Reviving
                || IsDead)
            {
                return;
            }

            // Handle aerial animations
            if (!Grounded)
            {
                UpdateAerialAnimations();
                return;
            }

            // Grounded: hand off to the concrete character's body animation.
            if (Grounded)
            {
                UpdateCharacterAnimation();
            }
        }

        /// <summary>
        /// Draw the grounded body for this frame: move, aim, shoot pose. Empty here because it is the one
        /// thing that genuinely differs per character. Milky drives the two-part top and bottom split;
        /// Grim drives a single-body three-facing set. Called every frame while grounded and not jumping,
        /// with the current move input and <see cref="_shootDirection"/> already sampled.
        /// </summary>
        protected virtual void UpdateCharacterAnimation() { }

        /// <summary>
        /// Throwing. Grim plants, winds up, and the bomb leaves his hand on the frame the art opens it.
        ///
        /// The release is read off the animation rather than run on a timer beside it, so the thing the
        /// player sees and the thing the game does cannot drift apart. Same principle as the Gob Grunt's
        /// charge under T-236, and the same trap avoided: the frame counter reports whichever clip is
        /// playing, so it waits to see the throw clip at frame 0 before counting its frames. Without
        /// that, whatever was playing a moment ago decides when the bomb leaves.
        /// </summary>
        void StateThrowing(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    ResetWalk();
                    SetMoveDirection(Vector2.zero);
                    _throwStarted = false;
                    _thrown = false;
                    PlayThrowVisual(_throwDirection);

                    // Remember which clip the character actually put on, so everything below can ask
                    // about that one rather than about "whatever is playing". Without this the state
                    // reads the clip it replaced: the walk was on frame 0 and already finished, so the
                    // bomb left on the first frame and the throw ended before it had drawn anything.
                    _throwClip = m_entityAnimator != null ? m_entityAnimator.CurrentAnimationName : null;
                    break;

                case Fsm.StateStep.Update:
                    // Rooted for the throw. Committing to it is what makes it cost something.
                    SetMoveDirection(Vector2.zero);

                    // No clip means no throw to read; leave rather than guess at timings.
                    if (m_entityAnimator == null || string.IsNullOrEmpty(_throwClip))
                    {
                        m_fsm.ChangeState((int)EntityState.Idle);
                        break;
                    }

                    // Only ever judge the throw clip itself.
                    if (m_entityAnimator.CurrentAnimationName != _throwClip)
                        break;

                    _throwStarted = true;

                    if (!_thrown && m_entityAnimator.CurrentFrame >= throwReleaseFrame)
                    {
                        _thrown = true;
                        ReleaseBomb();
                    }

                    // Back to normal once the follow-through has played out. The bomb has long since
                    // left by then, so the tail is purely the recovery the art draws.
                    if (_throwStarted && m_entityAnimator.IsDone)
                        m_fsm.ChangeState((int)EntityState.Idle);
                    break;
            }
        }

        /// <summary>
        /// Puts a bomb in the air, aimed a fixed distance along the throw direction.
        ///
        /// A fixed distance rather than at the nearest enemy: a bomb that homed would remove the aiming
        /// from a weapon whose whole cost is that you have to place it, and the eight-direction aim is
        /// already the thing that makes positioning the skill.
        /// </summary>
        void ReleaseBomb()
        {
            if (bombPrefab == null || PoolManager.Instance == null)
                return;

            GameObject spawned = PoolManager.Instance.Acquire(bombPrefab.name, Position, Quaternion.identity);
            if (spawned == null)
            {
                // Loud, because the failure is otherwise invisible: the animation plays in full and
                // simply nothing comes out of it.
                Debug.LogError($"[Player] No '{bombPrefab.name}' available to throw; the pool is missing or spent.", this);
                return;
            }

            Bomb bomb = spawned.GetComponentInChildren<Bomb>(true);
            if (bomb == null)
                return;

            bomb.Init();

            // Credited to the thrower, so its blast and shrapnel count as this player's hits. Enemies
            // already stamp theirs; the player's throw was the one that did not.
            bomb.Instigator = this;
            bomb.Position = Position;
            bomb.Lob((Vector2)Position + _throwDirection.normalized * throwDistance);

            // Spend the bomb only once one is actually in the air, so a missing pool never eats a
            // count. The gate in HandleThrowInput keeps this from going negative.
            if (_bombsRemaining > 0)
                _bombsRemaining--;
        }

        /// <summary>
        /// Play the throw, facing <paramref name="direction"/>. Empty here so each character supplies its
        /// own art; the timing and the projectile are the same whoever is throwing.
        /// </summary>
        public virtual void PlayThrowVisual(Vector2 direction) { }

        /// <summary>
        /// Play the leap pose at takeoff, facing <paramref name="direction"/>. Empty here so each character
        /// supplies its own jump art; the height and airtime are the motor's arc regardless of the clip.
        /// </summary>
        protected virtual void PlayJumpVisual(Vector2 direction) { }

        protected override Vector2 GetAnimationFacingDirection()
        {
            // Player prioritizes shoot direction, then movement, then current facing
            if (_shootDirection.sqrMagnitude > 0.0001f)
                return _shootDirection;

            if (m_moveInput.sqrMagnitude > 0.0001f)
                return m_moveInput;

            return FacingDirection;
        }

        public override string GetDebugInfo()
        {
            string baseInfo = base.GetDebugInfo();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(baseInfo);

            sb.AppendLine("─────────────────────");
            sb.AppendLine("<b>PLAYER</b>");
            sb.AppendLine($"Shoot Dir: {_shootDirection.x:F2}, {_shootDirection.y:F2}");
            sb.AppendLine($"Walk: {_walkSpeed:F0} px/s along {_walkDirection.x:F2}, {_walkDirection.y:F2}");
            sb.AppendLine($"Height: {(motor != null ? motor.Height : 0f):F1}   Grounded: {Grounded}");

            if (m_basicGun)
                sb.AppendLine($"Gun: {m_basicGun.name}");

            if (_invulnTimer > 0f)
            {
                bool frozen = m_fsm?.CurrentState == (int)PlayerState.Damaged;
                sb.AppendLine($"<color=yellow>INVINCIBLE {(_invulnTimer):F2}s ({(frozen ? "hurt" : "blink")})</color>");
            }

            if (m_fsm?.CurrentState == (int)PlayerState.Jump)
            {
                sb.AppendLine("<color=cyan>JUMPING</color>");
                sb.AppendLine($"Jump Dir: {_jumpDirection.x:F2}, {_jumpDirection.y:F2}");
            }

            return sb.ToString();
        }

        protected override string GetCurrentStateName()
        {
            if (m_fsm == null)
                return "No FSM";

            return m_fsm.CurrentState switch
            {
                (int)PlayerState.Jump => "Jump",
                (int)PlayerState.Damaged => "Damaged",
                (int)PlayerState.Reviving => "Reviving",
                (int)EntityState.Idle => "Idle",
                (int)EntityState.Dead => "Dead",
                _ => $"Custom ({m_fsm.CurrentStateName})",
            };
        }
    }
}
