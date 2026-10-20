# My Games

Games for Android phones, one per folder.

| Game | Folder | What it is |
| --- | --- | --- |
| Graveway (zombie shooter) | [`graveway/`](graveway) | Plain ES modules and three.js, no build step. Runs in a browser and as an Android app |
| Graveway for Unity (port in progress) | [`graveway-unity/`](graveway-unity) | Unity 6 project, ported from the web version one system at a time |

Graveway moved here from the
[MyApps](https://github.com/enachealex/MyApps) repo with its history.

## Playing the web version locally

```bash
npx serve .
# then open http://localhost:3000/graveway/
```

## Android builds

**Actions → Build Android APK → Run workflow** builds an APK from a web
game's folder (default `graveway`) and attaches it to a Release. The build
is [`tools/android/build-apk.mjs`](tools/android/build-apk.mjs), which you
can also run on your own computer. See
[`graveway/ANDROID.md`](graveway/ANDROID.md) for both.

The Unity port builds from the Unity editor for now. See
[`graveway-unity/README.md`](graveway-unity/README.md).
