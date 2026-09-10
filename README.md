# com.takoboystudios.topdown2d

The entity, movement and combat foundation for top-down 2D games.

Named for what it is. Roughly half of this is genuinely top-down and a platformer could not use it:
the eight-direction motor, eight-direction aim, grid navigation, the directional hurtbox and the
Y-sorters. `Entity`'s height model is the top-down hop, where height is defensive decoration rather
than platformer gravity. The other half (health and hit lag, the damage structs, hit and hurt boxes,
the animator bridge, projectiles and effects) is genre-neutral, so the name under-sells it. That is
the better error.

## What is in it

| Area | Types |
|---|---|
| Model | `Entity`, `PhysicsEntity`, `EntityComponent`, `Character`, `Enemy`, `Player` |
| Combat | `DamageInfo`, `DamageValues`, `HitEvent`, `Hitbox2D`, `Hurtbox2D`, `DirectionalHurtbox` |
| Movement | `TopDownMotor2D`, `PhysicsBox` |
| Navigation | `NavGrid`, `Pathfinder`, `PathFollower`, `Nav` |
| Presentation | `EntityAnimator`, `HitFlash`, the isometric sorters, `CameraShakePreset`, `DebugDraw` |
| Content bases | `Projectile`, `Effect`, `DebrisPiece` |
| Statuses | `StatusDefinition`, `StatusHolder`, `StatusEvents` |

## What the game supplies

The package names no game's concepts. Where it needs a rule it does not own, it asks, and every hook
defaults to "no opinion" so the package works standalone.

- `CombatRules.WeaknessRule` — whether one element beats another. Unset, nothing beats anything.
- `CombatRules.ExtraContainmentMask` — extra layers a contained body collides with.
- `CombatRules.HeavyHitBreaks` — statuses a heavy hit removes.
- `ScreenShake.Handler` — what to do about a shake request.
- `EntityEvents` — a player spawned, a player was hurt, an enemy was killed. Subscribe rather than
  having the entity layer call into your systems.

## Statuses

The package runs statuses but defines none. A game authors `StatusDefinition` assets: duration,
stack cap, refresh mode, damage over time, damage on displacement, and three multipliers cover most
of them as pure data. Anything with real behaviour subclasses the definition and overrides
`CanApply`, `OnApplied`, `OnReachedCap`, `OnEnded` or the multipliers.

`StatusHolder` has a generic cooldown table, which is enough to build an anti-stunlock window without
the package knowing what a freeze is.

## Dependencies

`com.takoboystudios.core`, `com.takoboystudios.animation`, Unity Input System, ZString, and Odin
Inspector, which is a hard dependency rather than a guarded one.
