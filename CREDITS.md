# Credits — third-party assets and data

The code and the drone model are Team TFC's (MIT, see LICENSE). These keep their own terms:

| What | Where it is | Source and license |
|---|---|---|
| Person model | `unity/Assets/JAMRA/Resources/JamraPerson.fbx` | CesiumMan, © 2017 Cesium, CC BY 4.0 (KhronosGroup glTF-Sample-Assets) |
| Animal model | `unity/Assets/JAMRA/Resources/JamraFox.fbx` | Fox, © 2014 tomkranis (rig/animation), © 2017 AsoboStudio & scurest (glTF), CC BY 4.0 (KhronosGroup glTF-Sample-Assets) |
| Arabic UI font | `unity/Assets/JAMRA/Resources/JamraUIFont.ttf` | DejaVu Sans, Bitstream Vera / DejaVu license (file next to it) |
| Ground image | `unity/Assets/JAMRA/Data/BargashSat.jpg`, `GroundTex.asset` | Sentinel-2 cloudless by EOX IT Services GmbH (s2maps.eu), CC BY 4.0; contains modified Copernicus Sentinel data |
| Land cover / forest | `BargashWorldCover.png`, `ForestMask.asset` | ESA WorldCover 2021, CC BY 4.0 |
| Terrain | `bargash_terrain.json`, `BargashTerrainData.asset` | SRTM 30 m (NASA / USGS), public domain |
| Buildings and roads | built into the scene | © OpenStreetMap contributors, ODbL |
| Map tiles in the reports page | loaded live, not stored | OpenStreetMap; Esri World Imagery (Esri, Maxar, Earthstar Geographics) |
| Camera detectors (fetched, not stored) | `web/get_models.sh` | YOLOv8 by Ultralytics, AGPL-3.0, via [jamra-fire-smoke](https://github.com/qudah269-ops/jamra-fire-smoke) and [jamra-life-lock](https://github.com/qudah269-ops/jamra-life-lock) |
| In-browser inference (fetched, not stored) | `web/get_models.sh` | onnxruntime-web 1.20.1, MIT |
| Engine | `web/Build/` | Built with Unity 6000.3.28f1 (Unity Web runtime under the Unity terms) |
