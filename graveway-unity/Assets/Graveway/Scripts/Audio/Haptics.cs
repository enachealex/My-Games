// Phone vibration: audio.js's vibrate(), which calls navigator.vibrate() on
// the web. On Android it goes through the system Vibrator. It does nothing in
// the editor, on other platforms, or with Enabled off.

using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Graveway
{
    public static class Haptics
    {
        public static bool Enabled = true;

        // One buzz `ms` long. 0 stops a buzz, as navigator.vibrate(0) does.
        public static void Vibrate(int ms)
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (ms <= 0) Cancel();
            else OneShot(ms);
#endif
        }

        // A pattern, as navigator.vibrate([on, off, on, ...]): buzz, pause,
        // buzz. An empty one stops a buzz.
        public static void Vibrate(params long[] pattern)
        {
            if (!Enabled || pattern == null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android's waveform starts with a pause, so it leads with a
            // zero-length one.
            var timings = new long[pattern.Length + 1];
            long total = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                timings[i + 1] = System.Math.Max(0, pattern[i]);
                total += timings[i + 1];
            }
            if (total <= 0) Cancel();
            else Waveform(timings);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const int DefaultAmplitude = -1;   // VibrationEffect.DEFAULT_AMPLITUDE
        const int NoRepeat = -1;

        static AndroidJavaObject vibrator;
        static AndroidJavaClass effects;
        static bool broken;

        // The Vibrator, found once: through VibratorManager from Android 12
        // (API 31), straight from the context before that. VibrationEffect is
        // there from API 26, the lowest this game runs on.
        static bool Ready()
        {
            if (vibrator != null) return true;
            if (broken) return false;
            try
            {
                var context = AndroidApplication.currentActivity;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") >= 31)
                    {
                        using var manager = context.Call<AndroidJavaObject>("getSystemService", "vibrator_manager");
                        vibrator = manager.Call<AndroidJavaObject>("getDefaultVibrator");
                    }
                    else vibrator = context.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                effects = new AndroidJavaClass("android.os.VibrationEffect");
                if (vibrator == null) broken = true;
                return vibrator != null;
            }
            catch (System.Exception)
            {
                broken = true;
                vibrator = null;
                return false;
            }
        }

        static void OneShot(long ms)
        {
            try
            {
                if (!Ready()) { Fallback(); return; }
                using var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", ms, DefaultAmplitude);
                vibrator.Call("vibrate", effect);
            }
            catch (System.Exception) { Fallback(); }
        }

        static void Waveform(long[] timings)
        {
            try
            {
                if (!Ready()) { Fallback(); return; }
                using var effect = effects.CallStatic<AndroidJavaObject>("createWaveform", timings, NoRepeat);
                vibrator.Call("vibrate", effect);
            }
            catch (System.Exception) { Fallback(); }
        }

        static void Cancel()
        {
            try { if (Ready()) vibrator.Call("cancel"); }
            catch (System.Exception) { /* nothing to stop */ }
        }

        // Unity's own fixed-length buzz. Referencing it is also what makes
        // Unity add the VIBRATE permission to the Android manifest.
        static void Fallback() => Handheld.Vibrate();
#endif
    }
}
