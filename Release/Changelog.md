## v2.4.2 (unreleased):
- Fix not clearing custom trigger level start and end callbacks.

## v2.4.1:
- Change settings file save location to avoid losing it on updates
- Add XML and JSON settings classes with additional functionality
- Update mod icon

## v2.4.0:
- Keybindings can now have modifiers
- Fixed OnScreenLog not loading immediatly after game startup
- Added Custom Triggers library
- Added Custom Menu library
- Fixed UMM window destruction issue
- Removed TowInt, TwoFloat, FloatRange, Coutdown, HeroUnlockCollection and IsThisMod classes

## v2.3.1:
- Added RC Car to TestVanDammeAnimTypes.

## v2.3.0:
- Added TestVanDammeAnimExtensions.SetReviveSource(this TestVanDammeAnim, bool)
- Create UnitExtensions
- Added JsonConverter Type: ColorJsonConverter
- Added more options for keybindings
- Added functions for loading materials and audioclips
- Added functions for checking TestVanDammeAnim type

## v2.1.0:
- Added .ToGameObjects() extensions to Component[]
- Added PlayerHUDExtensions
- Added NetworkedUnitsExtensions
- Added Collections.Nums
- Added ComponentExtensions.ToGameObjects<T>(this Component[])
- Added ObjectExtensions.InvokeBaseMethod(this object) ; it's not great to use
- Removed ResourcesController.cs

## v2.0.1:
- replace unkown method 'RemoveAllFrom' to 'RemoveAll' in Collections.Heroes.CampaignBro

## v2.0.0 :
- Add Various Extensions for objects
- Create a 'Collections' Namespace to sort everything in 'HeroUnlockCollection'
- Make 'HeroUnlockCollection' obsolete
- Create class 'Heroes', 'Pickups' and 'PockettedSpecial' under 'Collections' namespace
- Added Some extensions
- Rename 'Main_' to 'Main'
- Remove 'RocketLibUMM' dependencie
- Added Newtonsoft.Json assembly
- Add Converters for Json serialization
