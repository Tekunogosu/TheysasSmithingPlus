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
- Fixed client and server sharing one cache in singleplayer.
- Fixed ingredient matching picking up tools that a recipe consumes.

### Internal

- Replaced LINQ on recipe and item paths with plain loops.
- Split recipe lookups and `AnvilPlacementMode` into their own files.
- Single owner for the collectible API field, with cached reflection lookups.


## AI dsclaimer

AI assistance was used in the updating of this mod. It was not "Vibecoded". I have well over 15 years of coding experience and that knowledge gets applied even when harnesing AI.
