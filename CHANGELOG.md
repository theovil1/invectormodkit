# Changelog

## 0.1.0 (2026-10-11)


### Features

* add the core plugin, the custom songs plugin and chart generator v2 ([468945e](https://github.com/theovil1/invectormodkit/commit/468945e7724c5c074711b45f19d1da26f08b01bf))
* **chartgen:** add a match-only mode and a fingerprint cache ([6f8c166](https://github.com/theovil1/invectormodkit/commit/6f8c1662dcaa78489a87e06e2e518a5765673d32))
* **charting:** add marketplace provenance and chart re-timing ([46f0ea1](https://github.com/theovil1/invectormodkit/commit/46f0ea1c377e098d5525105f860da6c337c35022))
* **charting:** add the charting library, evaluation tools and trained note model ([6f85f34](https://github.com/theovil1/invectormodkit/commit/6f85f34f2b126a47b2ac621c183d8b3ef0d4e36a))
* **charting:** enforce minimum note and turn spacing in generator v3 ([5605fed](https://github.com/theovil1/invectormodkit/commit/5605fed311cf3fb813650ae0c5571984a0948171))
* **charting:** favour sung attacks for holds in generator v5 ([3c55428](https://github.com/theovil1/invectormodkit/commit/3c554287ea8ce6750499dd007d3bcb4daca16054))
* **charting:** follow the vocals with Spleeter separation ([29eff5f](https://github.com/theovil1/invectormodkit/commit/29eff5fc1beb20388c7ad3acd3a1f7766fbd2859))
* **charting:** learn freefly zones and holds from the official charts ([c3ae4b7](https://github.com/theovil1/invectormodkit/commit/c3ae4b72c1af389f083609f6a475a0d1c0085c32))
* **charting:** make fast passages playable in generator v7 ([1540d40](https://github.com/theovil1/invectormodkit/commit/1540d402bfb153fb9a79997aa1c6ff43e4b52f70))
* **charting:** recognise songs and their offset with audio fingerprints ([7c20204](https://github.com/theovil1/invectormodkit/commit/7c20204bed818d35e83491a7bf873f6a1d2a1eda))
* **client:** read the latest GitHub release ([8ad8284](https://github.com/theovil1/invectormodkit/commit/8ad82844c2de6aa166464721deb3175060f562b7))
* **custom-songs:** match pending marketplace installs with ChartGen ([a6cf82a](https://github.com/theovil1/invectormodkit/commit/a6cf82a7b779d55e5023601bc30a5eda1e9cab83))
* **custom-songs:** pick up songs added or deleted while the game runs ([e7b02ee](https://github.com/theovil1/invectormodkit/commit/e7b02ee4b42cd5c73cb52c8afde8c0b6f04649fb))
* **custom-songs:** support marketplace charts and expose an API to other plugins ([a02b6b0](https://github.com/theovil1/invectormodkit/commit/a02b6b0f6f09cbe449d458f7b7fc0f02194d7d1a))
* **devtools:** export the UI tree and the Steam session ticket ([8f939e4](https://github.com/theovil1/invectormodkit/commit/8f939e43b2504bf24176f2edf414a65c441f4764))
* **editor:** add the editing model with node tests ([4ee1867](https://github.com/theovil1/invectormodkit/commit/4ee18674ecc13e6ed5ec801941a90779b81b0982))
* **editor:** add the editor app and hot reload of edited charts ([9108b3b](https://github.com/theovil1/invectormodkit/commit/9108b3b61f8587b6639128cae93157d53e094e16))
* **editor:** add the editor core with structured playability issues ([71ee5f0](https://github.com/theovil1/invectormodkit/commit/71ee5f07208f2a88132f33bb4136a4ba2eb69db7))
* **editor:** add the editor zip and its download page ([2cad7fc](https://github.com/theovil1/invectormodkit/commit/2cad7fc7fae2fa4d2a2e62a362841e4618b4a9f4))
* **editor:** export the game's look for the editor ([c30ed53](https://github.com/theovil1/invectormodkit/commit/c30ed53b462ad00938cd2cd232fa20f8162619f2))
* **editor:** redesign the editor with a game-styled layout and a 3D preview ([71076b0](https://github.com/theovil1/invectormodkit/commit/71076b0085333916704ae57311f3b5f2b630bbcd))
* **editor:** rename the chart editor to level editor ([07b5b50](https://github.com/theovil1/invectormodkit/commit/07b5b50c944d6155a8b22169151d25abeed2f60d))
* **editor:** show the player's keyboard keys and clear turn markers ([bbb2843](https://github.com/theovil1/invectormodkit/commit/bbb2843d89b1bbd25387ab37cd3c4b450feb39f9))
* **installer:** install, update and uninstall the mod in a game folder ([599a104](https://github.com/theovil1/invectormodkit/commit/599a104c6c0470bcdeb3965adf188136cd9b23e6))
* **marketplace:** add an animated Discord button and translate the status corner ([#3](https://github.com/theovil1/invectormodkit/issues/3)) ([be9b1ad](https://github.com/theovil1/invectormodkit/commit/be9b1adc3be480efd33ae5d68a76a739b8a687c9))
* **marketplace:** add an in-game window to browse, install and publish charts ([66b1f8a](https://github.com/theovil1/invectormodkit/commit/66b1f8a51933a654d8eeefc957ad3964fe66ef38))
* **marketplace:** add help text and hide withdrawn publications ([da2e3f3](https://github.com/theovil1/invectormodkit/commit/da2e3f3e8463efd22b1f931d3d590bb180f86ab9))
* **marketplace:** add leaderboards, reports and rules in game ([ba31a3e](https://github.com/theovil1/invectormodkit/commit/ba31a3e54ed8622e8ddd4d9c6cb56ee5cbfaba62))
* **marketplace:** add the marketplace client library ([b2111aa](https://github.com/theovil1/invectormodkit/commit/b2111aa1b3a506da49ce875b85f9e21134ed11b2))
* **marketplace:** give the window its own canvas, mouse support and a close button ([8ad566b](https://github.com/theovil1/invectormodkit/commit/8ad566bb5e5f4a7adb0da2416f196d08847d5ecd))
* **marketplace:** offer mod updates in the main menu ([555f5e2](https://github.com/theovil1/invectormodkit/commit/555f5e2d7a91cabf64c76e08d5c003487b14a4f7))
* **marketplace:** show the YouTube video a chart is timed on ([4de2416](https://github.com/theovil1/invectormodkit/commit/4de24167f0a29947a97ededeb0c4af6bb0d02b2a))
* **marketplace:** translate into all the game's languages and build the menu button natively ([b9e1051](https://github.com/theovil1/invectormodkit/commit/b9e1051e065fa72e37c55635ecb9337b6f8f63d9))
* **marketplace:** type links in a dialog and recognise your own charts ([b411937](https://github.com/theovil1/invectormodkit/commit/b4119371ae557e4ec7f21d5198cb34ad873b12a6))
* **marketplace:** use the game's question dialog for confirmations ([0216591](https://github.com/theovil1/invectormodkit/commit/0216591bb4c127273baa284b5140f8f68cf021be))
* open leaderboards and publishing to everyone ([40646d6](https://github.com/theovil1/invectormodkit/commit/40646d657f84a46ca2da066aa865e91487bb57f3))
* **server:** add the marketplace schema, search and download ([ec19617](https://github.com/theovil1/invectormodkit/commit/ec19617e39f62b233a4378c69f2e21a329089264))
* **server:** sign in with Steam through the browser ([6117f40](https://github.com/theovil1/invectormodkit/commit/6117f40250354b3b224c2fa0f36a819c1250af13))
* **setup:** add one-click installer with silent update and uninstall modes ([e6e04f1](https://github.com/theovil1/invectormodkit/commit/e6e04f11b5907d2695d8cd3e930c0547050733d0))


### Bug Fixes

* **custom-songs:** crop covers to squares like the official ones ([31e9130](https://github.com/theovil1/invectormodkit/commit/31e9130d4eee9e078cb7cfa8625b7aad01c45c22))
* **custom-songs:** keep full render resolution and never analyse songs during gameplay ([7f32a30](https://github.com/theovil1/invectormodkit/commit/7f32a306f1f5b2336be4f323fd94acfc7e799863))
* **custom-songs:** keep the song selected after a hot reload ([102c419](https://github.com/theovil1/invectormodkit/commit/102c419dbd41f084409bc85dc2f050e2f760054a))
* **custom-songs:** stop late previews in game and clean up song titles ([56b45f5](https://github.com/theovil1/invectormodkit/commit/56b45f58508cf2e5f0545284c24c7eaa3e104993))
* **marketplace:** capture all input while the window is open ([e41eccc](https://github.com/theovil1/invectormodkit/commit/e41eccc8cfd8b4d07f2d9db91039dd8071a89679))
* **marketplace:** keep inactive tabs from turning white ([231075d](https://github.com/theovil1/invectormodkit/commit/231075d8a87af06c76d6fc64c1053100defd6421))
* **marketplace:** raycast through the menu camera ([23936d6](https://github.com/theovil1/invectormodkit/commit/23936d6f68d606a0ceb9a8f76a42b546676248a6))
* **marketplace:** read the mouse directly in the window ([2080f35](https://github.com/theovil1/invectormodkit/commit/2080f351e59070c1d86064747a1c45757b872a43))
* **marketplace:** show the cursor and fix the tabs and background layout ([3bb258e](https://github.com/theovil1/invectormodkit/commit/3bb258ed68523bce0aa8c021eb984708ba16965a))
* **setup:** support game folders with paths over 260 characters ([b74af58](https://github.com/theovil1/invectormodkit/commit/b74af581abb6fd44a6bded7cbdb94f6ab6206b31))


### Performance Improvements

* **custom-songs:** generate charts out of process ([b522001](https://github.com/theovil1/invectormodkit/commit/b522001298553b6c1dfa680f80eae3115b60c2ad))
