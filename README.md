# SmithingPlus Forked

I created this fork to address many outstanding issues from the main repo which has gone a bit neglected at this point. 


## Fixes

- Removed the long freeze the first time you look at an anvil. Metal material
  lookups are cached, including "no metal" results, and recipes are indexed by
  ingredient instead of rescanned for every item. The freeze scaled with items
  multiplied by recipes, so large modpacks were greatly affected.
- Fixed a crash when hovering over an anvil recipe in the selector.
- Fixed a crash on items that are anvil-workable but declare no voxels.
- Fixed items with random voxel patterns placing a different shape than the one
  measured, so placement could fail or misplace.
- Chisels no longer hold for 1.5 seconds on every target; the hold now only
  applies when scraping a crucible.
- Iron blooms and other items stating their own workable temperature no longer
  recompute it every tooltip frame.
- Behaviours are now replaced rather than duplicated when re-added.
- Fixed the game's server and client sides sharing one cache of pre-calculated
  data in singleplayer, where both run inside the same program, so each could
  end up using the other's. They now each keep their own.
- Fixed ingredient matching picking up tools that a recipe consumes.
- Fixed hammer strikes sometimes acting twice, moving a voxel further than the
  hit should. The client and server disagreed over which tool mode meant "flip",
  so one click could flip on one side and hammer on the other.
- Fixed the hammer mode being stuck on Heavy Hit when "remember last hammer
  mode" is enabled. The remembered mode overrode every selection instead of only
  applying to a hammer that had no mode set.
- Fixed duplicate broken tool heads when another mod handles tool damage, such
  as Toolsmith. The head used to drop when a tool was *predicted* to break, so a
  Toolsmith tool dropped one whenever its handle or binding broke, not just its
  head. It now drops only when the tool is actually destroyed.
- Fixed a tool with no matching head recipe being searched for on every single
  break instead of once, since "no recipe" was never remembered.
- Fixed a crash and a divide-by-zero when a mold priced itself against a
  malformed smithing recipe.

### Internal

- Replaced LINQ on recipe and item paths with plain loops.
- Split recipe lookups and `AnvilPlacementMode` into their own files.
- Single owner for the collectible API field, with cached reflection lookups.
- Single owner for the voxel-cost calculation, which had been copied into three
  places and had drifted apart — one copy was missing the guards the other two
  had, which is where the mold crash above came from.
- Single owner for the command player/held-item lookup, previously copied five
  times.
- Moved the shattered-bits payout rule out of the stack utilities into the
  bits-recovery feature it belongs to.
- Dropped an array that was being built only to ask whether it was empty.

### Debug commands

Both require the `root` privilege, so they are unavailable to ordinary players
on a server.

- `/sp setDurability <n>` (`/sp dur`) — set the held item's remaining
  durability. `/sp dur 1` leaves it one hit from breaking.
- `/sp breakHeld` (`/sp brk`) — break the held item outright, through the same
  path a worn-out tool takes.


## AI disclaimer

AI assistance was used in the updating of this mod. It was not "Vibecoded". I have well over 15 years of coding experience and that knowledge gets applied even when harnessing AI.
