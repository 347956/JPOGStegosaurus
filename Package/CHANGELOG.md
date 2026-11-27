## 1.0.6
Bug fixes and cleanup.
- Detection areas:
	- The front, back and aggro areas now rotate with the Stegosaurus, so the area behind it is actually behind it.
- Tail spikes:
	- Players killed by the tail now have a 40% chance to be cut in half
	- The body on the spike now drips blood.
	- The body can be grabbed from the spike and falls off when the Stegosaurus dies.
	- Fixed the body being pinned to a different spike for each player.
- Special tail attack:
	- Other enemies are now hit once per attack and the attack no longer skips enemies that can be killed.
- Safety:
	- The Stegosaurus can no longer target or hit players in the ship or in the Company Cruiser seats, this includes its contact damage.
- Stun:
	- Stunning the Stegosaurus as a client now works properly.
- Roaming:
	- The Stegosaurus now walks to points that are further away and mostly in front of it, so it zigzags a lot less.
	- The Stegosaurus remembers its last 5 to 10 points and will not walk back to one of them.
	- Added the config entries "Min Roam Distance" and "Max Roam Distance" to tweak how far the next point is.
- Chasing:
	- Fixed the irritation decrease using the wrong value.
- Network:
	- Greatly reduced the amount of network messages sent while the Stegosaurus is active.
- Removed the unused config sync code.

## 1.0.5
Fixed the Stegosaurus for V81
Switched to DawnLib

## 1.0.4
Fixed the Stegosaurus for V73
- The StegoSaurus now has a few extra collision boxes, this should make it easier to hit him with the shotgun for example.
- Added an extra hit check to the Stegosaurus stomp and tail attack.
	- The Stegosaurus should no longer be able to hit you with these attacks when in the driver or pasenger seat of the Company Cruiser (cargo remains unsafe).

## 1.0.3
Fixed the Stegosaurus for V70

## 1.0.2
Some small Tweaks.
- Updated the NavMeshAgent
	- This should improve the navigation of the Stegosaurus and where it can and cannot go.
- Reduced "NormalizedTimeInDayToLeave" value.
	- the Stegosaurus should now despawn at the set time.
- Added a Config entry where the "NormalizedTimeInDayToLeave" can be set for the Stegosaurus.


## 1.0.1
Some small fixes.
- Creature voice:
	- Stegosaurus's voice/roar should no longer be heard across the map and inside the facility
- Back area detection:
	- Changed the width of the collision box that determines if players are behind the Stegosaurus and near it's tail from 5 to 3. 
	- This should make it a bit safer to walk around the Stegosaurus.
- Spawn count:
	- Reduced the max amount of Stegosaurus that can spawn from 5 to 3.
	- Added a setting that allows you to set the max amount of Stegosaurus that can spawn naturally. 
- Bestiary:
	- Added some extra Trivia from the JPOG wiki.

Thanks for providing some feedback in the discord so I was able to notice/become aware of these issues and fix them so quickly!

## 1.0.0
- Initial release