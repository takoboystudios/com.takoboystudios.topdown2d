using System;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// One of a player's loadout slots (Grip, Knuckle): the abilities this character can put in it,
    /// which one is in it now, and how many uses it has left. Written once and used by every slot, so
    /// equipping, swapping and spending are the same code whichever slot it is.
    ///
    /// Per player: it lives on the <see cref="Player"/>, never on the shared <see cref="SlotDefinition"/>,
    /// so two Grims carrying the Bomb each have their own count (CLAUDE.md, co-op).
    ///
    /// Nothing here allocates. Equipping only picks from the options, which the player pooled at setup.
    /// </summary>
    [Serializable]
    public class LoadoutSlot<T> where T : SlotDefinition
    {
        [Tooltip(
            "What this character can put in the slot. The first is what they start a run with, until the "
                + "Tavern's equip screen exists; the debug overlay can cycle through the rest. Empty for a "
                + "character with nothing in this slot, and its button does nothing."
        )]
        [SerializeField]
        T[] options;

        T _equipped;
        int _uses;

        /// <summary>What is in the slot, or null.</summary>
        public T Equipped => _equipped;

        /// <summary>Uses left. Negative when the equipped ability never runs out.</summary>
        public int Uses => _uses;

        /// <summary>Something is equipped and it has a use left.</summary>
        public bool Ready => _equipped != null && _uses != 0;

        /// <summary>How many abilities this character can put in the slot. Walk them with <see cref="At"/>.</summary>
        public int Count => options != null ? options.Length : 0;

        public T At(int index) => options[index];

        /// <summary>Equips the first option, or clears the slot when there are none. What a run starts with.</summary>
        public void EquipFirst() => Equip(Count > 0 ? options[0] : null);

        /// <summary>
        /// Puts an ability in the slot with a fresh supply of uses. Equipping is a choice made before a
        /// run (the Tavern), so a fresh supply is right; the debug overlay uses the same call, which is
        /// also how a test refills. An ability this character does not list is refused, because nothing
        /// pooled what it spawns.
        /// </summary>
        public bool Equip(T ability)
        {
            if (ability != null && IndexOf(ability) < 0)
            {
                Debug.LogWarning($"[LoadoutSlot] '{ability.name}' is not one of this slot's options, so it was not equipped.");
                return false;
            }

            _equipped = ability;
            _uses = ability != null ? ability.Uses : 0;
            return true;
        }

        /// <summary>Equips the next option, wrapping round, refilled. For testing; the real choice is made in the Tavern.</summary>
        public void Cycle()
        {
            int count = Count;
            if (count == 0)
                return;
            Equip(options[(IndexOf(_equipped) + 1) % count]);
        }

        /// <summary>
        /// Spends one use. Does nothing to an ability that never runs out, or one already at zero, or
        /// while <see cref="TestCheats.InfiniteUses"/> is on.
        /// </summary>
        public void Spend()
        {
            if (_uses > 0 && !TestCheats.InfiniteUses)
                _uses--;
        }

        /// <summary>
        /// Gives uses back, up to the equipped ability's <see cref="SlotDefinition.MaxUses"/>: a rest
        /// campfire returning a Mega Riff, a pickup. False when nothing changed, because nothing is
        /// equipped, it never runs out, or it is already full.
        /// </summary>
        public bool Restore(int amount)
        {
            if (_equipped == null || _uses < 0 || amount <= 0)
                return false;

            int most = _equipped.MaxUses;
            if (_uses >= most)
                return false;

            _uses = Mathf.Min(most, _uses + amount);
            return true;
        }

        public int IndexOf(SlotDefinition ability)
        {
            for (int i = 0; i < Count; i++)
            {
                if (options[i] == ability)
                    return i;
            }
            return -1;
        }
    }
}
