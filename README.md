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
| `Editor/` | The Unity editor script that builds this mod's bundles (menu JPOG > Build JPOGStegosaurus bundles). |
| `Plugin~/` | The C# plugin. The `~` is on purpose: Unity ignores folders ending in `~`, so it doesn't try to compile the BepInEx code. |
| `AssetSources~/` | Source files (Blender) that Unity shouldn't import. |

## Building 🔨

1. Create `Plugin~/JPOGStegosaurus.csproj.user` (gitignored, machine specific). It sets the game path, copies the netcode-patched DLL to `AssetBundles/` (where Unity finds the AI script for the prefab) and deploys the mod to your test setup. A minimal version:

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

   To deploy, add a target after `CopyToUnity` that copies `AssetBundles/JPOGStegosaurus.dll` and `AssetBundles/*.duskmod` to `BepInEx/plugins/347956-JPOGStegosaurus/` of your r2modman profile, and the content bundle to an `Assets/` subfolder there (Dusk only loads content bundles from `Assets/`).

2. In `Plugin~`, run `dotnet tool restore` once, then `dotnet build`.
3. Build the bundles in Unity with **JPOG > Build JPOGStegosaurus bundles** (`Editor/JPOGStegosaurusBundleBuilder.cs`). It builds only this mod's bundles into `AssetBundles/`; the Asset Bundle Browser would build every bundle in the project into its one output folder. Run `dotnet build` again afterwards to deploy the new bundles.

> [!IMPORTANT]
> Build the DLL before opening the Unity project after a fresh clone. If the DLL is missing, Unity deletes its `.meta` file and the prefab loses its AI script.

### GitHub Actions ⚙️

- **Build and test** (`.github/workflows/build.yml`) runs on every push and pull request to `main`. It runs the xUnit projects in `Tests~/` (Debug and Release), builds the plugin with `CI=true`, which takes the game assemblies from the `LethalCompany.GameLibs.Steam` NuGet package, and packs the Thunderstore ZIP. The ZIP and the Debug DLL are uploaded as run artifacts.
- **GitHub release** (`.github/workflows/release.yml`) runs on every push to `main`. If the tag `v<Version>` for the `<Version>` in `Plugin~/JPOGStegosaurus.csproj` doesn't exist yet, it tests, builds and checks the package, then creates the tag and a GitHub Release with the ZIP attached. It doesn't upload to Thunderstore.
- **Thunderstore publish** (`.github/workflows/thunderstore.yml`) runs only by hand (Actions > Thunderstore publish > Run workflow, with a tag such as `v1.2.0`). It downloads the ZIP from that GitHub Release, checks it, and uploads it with tcli using the `TCLI_AUTH_TOKEN_JPOGSTEGOSAURUS` secret. It skips versions already on Thunderstore. Test the GitHub Release ZIP in game before running it.

  To release, raise `<Version>` in the csproj and the version in `JPOGStegosaurusModInformation.asset` together, add a `## <version>` entry to `Package/CHANGELOG.md`, and rebuild and commit the bundles. The workflow stops if any of these is missing. You can also push a `v<version>` tag, or run the workflow by hand with an existing tag to retry.

CI builds can't build the Unity bundles. They package the bundles committed in `AssetBundles/`.

## Making The Mod Yours

### Thunderstore Packages

We have configured [JPOGStegosaurus.csproj](/Plugin~/JPOGStegosaurus.csproj) to build a Thunderstore package to [/Plugin~/Thunderstore/Packages/](/Plugin~/Thunderstore/Packages/) using [tcli](https://github.com/thunderstore-io/thunderstore-cli/wiki) each time we make a release build of our mod. A release build can be done for example from the command-line like this: `dotnet build -c release`. This will use configuration options from [thunderstore.toml](/Plugin~/Thunderstore/thunderstore.toml), so configure it for your own mod! Uploads go through the **Thunderstore publish** workflow above. Keep `[package.dependencies]` in line with what the mod needs at runtime (DawnLib).

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