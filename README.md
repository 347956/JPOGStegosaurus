# JPOGStegosaurus

This repository contains the full source code for the JPOGStegosaurus for Lethal Company, including its Unity content (prefab, model and Dusk definitions). The guide part of this project can be found at https://lethal.wiki/dev/apis/lethallib/custom-enemies/overview.

> [!NOTE]
> The wiki page might be slightly out of date regarding how the JPOGStegosaurus prefab is constructed.

## Repository layout 📁

This repository is a folder that goes inside the `Assets/` folder of a Lethal Company Unity project, for example `Assets/LethalCompany/Mods/plugins/JPOGStegosaurus/`. It holds all of the mod's own Unity content and its code. The host Unity project supplies the generic parts: the game scripts (Assembly-CSharp), the Unity packages and DawnLib (`com.github.teamxiaolan.dawnlib.dusk.dll`).

| Path | Contents |
| --- | --- |
| `Content/JPOGStegosaurus/` | The enemy: prefab, model, animations, materials, audio, bestiary entries and Dusk registry assets. |
| `JPOGStegosaurusContentContainer.asset`, `JPOGStegosaurusModInformation.asset` | The Dusk content container and mod information for the Dusk export. |
| `Package/` | README, changelog and icon of the Thunderstore package (referenced by the mod information). |
| `AssetBundles/` | Build output: the plugin DLL and the exported bundles. Only the `.meta` files are committed; the prefab references the DLL by the GUID in its `.meta`. |
| `Plugin~/` | The C# plugin. The `~` is on purpose: Unity ignores folders ending in `~`, so it doesn't try to compile the BepInEx code. |
| `AssetSources~/` | Source files (Blender) that Unity shouldn't import. |

## Building 🔨

1. Create `Plugin~/JPOGStegosaurus.csproj.user` (gitignored, machine specific) with your game path and a target that copies the DLL to `AssetBundles/`:

   ```xml
   <Project>
     <PropertyGroup>
       <GameDirectory>/path/to/Lethal Company/</GameDirectory>
       <ManagedDirectory>$(GameDirectory)Lethal Company_Data/Managed/</ManagedDirectory>
     </PropertyGroup>
     <Target Name="CopyToUnity" AfterTargets="NetcodePatch">
       <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(MSBuildThisFileDirectory)../AssetBundles/" />
     </Target>
   </Project>
   ```

2. In `Plugin~`, run `dotnet tool restore` once, then `dotnet build`. The netcode-patched DLL is copied to `AssetBundles/`, where Unity finds the AI script for the prefab.

> [!IMPORTANT]
> Build the DLL before opening the Unity project after a fresh clone. If the DLL is missing, Unity deletes its `.meta` file and the prefab loses its AI script.

## Making The Mod Yours

### Thunderstore Packages

We have configured [JPOGStegosaurus.csproj](/Plugin~/JPOGStegosaurus.csproj) to build a Thunderstore package to [/Plugin~/Thunderstore/Packages/](/Plugin~/Thunderstore/Packages/) using [tcli](https://github.com/thunderstore-io/thunderstore-cli/wiki) each time we make a release build of our mod. A release build can be done for example from the command-line like this: `dotnet build -c release`. This will use configuration options from [thunderstore.toml](/Plugin~/Thunderstore/thunderstore.toml), so configure it for your own mod! Do note that we have not included a way to upload your mod to thunderstore via tcli in this project.

### Renaming The Mod

Renaming a mod can easily break things if you don't update every instance of it. If you want to rename your mod, these are some of the things you need to worry about:
- rename `csproj` files
- rename solution file, with its references to your new csproj filename
- When renaming [JPOGStegosaurusAI.cs](/Plugin~/src/JPOGStegosaurusAI.cs) and its class, make sure to build your mod DLL (it is copied to `AssetBundles/`) and add your new AI class as a component to your enemy prefab. If the class doesn't appear as an option, make sure your mod DLL doesn't have any errors in the Unity editor console. If your mod depends on other mods, make sure their DLL files are in the host Unity project.
    - You might have to reapply all your configuration settings for the AI script. You need to be very careful to not miss a configuration option, as missing something can break the enemy's behavior or lead to errors.
- If your IDE complains about invalid references, try restarting it. If this does not fix it, you probably have forgotten to rename something.

> [!TIP]  
> You can use `Ctrl` + `Shift`+ `F` to search every file in both Visual Studio and Visual Studio Code. This can for example help you find every instance of `JPOGStegosaurus` inside files in the whole project. Do note however that this does not apply to filenames.  
> In Visual Studio, you can use `Ctrl` + `,` to search files by name. In Visual Studio Code you can use `Ctrl` + `P` to do the same.

## Credits

[EvaisaDev](https://github.com/EvaisaDev) - [LethalLib](https://github.com/EvaisaDev/LethalLib)  
[Lordfirespeed](https://github.com/Lordfirespeed) - reference tcli usage in LethalLib  
[Xilophor](https://github.com/Xilophor) - csproj files taken from Xilo's [mod templates](https://github.com/Xilophor/Lethal-Company-Mod-Templates)  
[XuuXiao](https://github.com/XuuXiao/) - porting LC-ExampleEnemy for LC v50  
[nomnomab](https://github.com/nomnomab) - [Lethal Company Project Patcher](https://github.com/nomnomab/lc-project-patcher) - used for the Unity Project  
[AlbinoGeek](https://github.com/AlbinoGeek) - issue template  
Melavex - suggestions and feedback on this project  
...and everyone who has helped with this project!