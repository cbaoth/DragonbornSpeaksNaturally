# How to Use Item Name Mapping

> Wrote by Odie in his [Github pull request](https://github.com/DougHamil/DragonbornSpeaksNaturally/pull/6)
> and edited by SwimmingTiger.

This is a collection of features aimed at simplifying equipping items from the favorites menu.
It can improve the voice equipment experience of items after being renamed by mods such as VIS.

It can also be used to rename items in the favorites menu voice-equip.

Copy `item-name-map.SAMPLE.json` to `item-name-map.json` to enable this feature. Edit `item-name-map.json` for your need.

## Usage examples

* Say `equip wither` for to equip `[A2] Wither` (works for any VIS renamed items)
* Say `equip healing` for `[R1 Heal Self I: Slow` (uses user accessible data file for mapping)
* Say `equip axe` to equip the first axe in the favorites menu (works for many types of equipment)
* Say `equip flames` to automatically equip spell in a configurable `main hand`

## Details

1. Clean items names of tags/symbols introduced by VIS

   This makes switching to these tagged items actually possible.
   As an example, the a spell like `[A2] Wither` can now be equipped by saying `equip wither`.

2. Map specific item names to a new phrase

   VIS renames some spells that makes it very difficult to say.
   For example, the `Healing` spell is renamed as `[R1] Heal Self I: Slow`.
   There is simply no regex that can possibly reverse this sort of renaming.

   To deal with this, we read from a data file `item-name-map.json` that can
   be used to reverse the renaming done by VIS.
   The sample file `item-name-map.SAMPLE.json` already includes all of the spells that are
   renamed in a fashion where cleaning via regex cannot derive the original name of the spell.

   In any case, this enable selecting the spell `[R1] Heal Self I: Slow` by saying `equip Healing`.

3. Select weapon by type

   In my current playthrough, I'm using the weapons: `Ancient Nord Hero War Axe` and `Ancient Nord Supple Bow of Ice`.
   As you can imagine, these are extremely hard to say, especially in the middle of combat.

   The new feature scans item names for certain equipment type keywords.
   So if an item has the word `axe` in it, it is then accessible by saying `equip axe`.
   At the moment, this works for `daggar`, `mace`, `sword`, `axe`, `battleaxe`, `greatsword`, `warhammer`,
   `bow`, `crossbow`, and `shield`.

   You can modify or extend the key word list by editing the `knownEquipmentTypes` option in `DragonbornSpeaksNaturally.ini`.

   The caveat here is that it will only record & use the first item of a type of equipment.
   So, if the user has two axes or two bows, in their favorites menu, only the **first** axe
   and **first** bow will be accessible this way.

4. Configurable default/main hand

   While exploring dungeons, I found myself frequently trying to cast Candlelight.
   The current default behavior of dsn would put candlelight in both hands.
   This means in the middle of exploring the dungeon, when candlelight expires,
   I'd have to do this in sequence:

   * equip candlelight
   * [cast spell]
   * equip axe
   * equip shield

    This feature introduces the idea of a `main hand`.
    Things are usually placed in the main hand unless the user specifically asks for the item to be placed
    in `left`, `right`, or `both` hands.
    This makes it easy to switch between weapons and spells while not messing with whatever is equipped in my left.
    Not only does this simplify the previous equip sequence, now I can freely switch between axe/bow/spell in
    the middle of combat with minimal cognitive overhead.

    You can change the mainhand by editing the `mainHand` option in `DragonbornSpeaksNaturally.ini`.
