#!/bin/bash
# Paths: set VALHEIM_MANAGED to Valheim/valheim_Data/Managed and BEPINEX to a profile's BepInEx folder.
M="${VALHEIM_MANAGED:-/mnt/user-data/uploads/Managed}"; B="${BEPINEX:-/mnt/user-data/uploads/Potato Server-Mods/BepInEx}"
CSC="${CSC:-dotnet /usr/lib/dotnet/sdk/8.0.131/Roslyn/bincore/csc.dll}"
mkdir -p out
$CSC -nologo -nostdlib -noconfig -target:library -optimize+ -deterministic -langversion:9 -out:out/PotatoDye.dll -r:"$M/mscorlib.dll" -r:"$M/netstandard.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" -r:"$M/assembly_valheim.dll" -r:"$M/assembly_utils.dll" -r:"$M/assembly_guiutils.dll" -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.InputLegacyModule.dll" -r:"$M/UnityEngine.PhysicsModule.dll" -r:"$M/UnityEngine.TextRenderingModule.dll" -r:"$M/Unity.TextMeshPro.dll" -r:"$M/UnityEngine.UI.dll" -r:"$M/UnityEngine.UIModule.dll" -r:"$M/UnityEngine.ParticleSystemModule.dll" -r:"$M/UnityEngine.AnimationModule.dll" -r:"$M/UnityEngine.AIModule.dll" -r:"$M/Unity.Collections.dll" -r:"$B/core/BepInEx.dll" -r:"$B/core/0Harmony.dll" src/*.cs
