using System;

namespace KardashevAscent.Presentation.Audio
{
    /// <summary>The game's sound effects, by what they mean.</summary>
    public enum Sfx
    {
        Click,
        Place,
        BuildComplete,
        Correct,
        Wrong,
        Beep,
        Alert,
        Milestone,
        Discovery,
        Open,
        /// <summary>A soft tick when the pointer moves onto a menu button.</summary>
        Hover,
        /// <summary>A rising rush of air (menu transitions, the intro).</summary>
        Whoosh,
        /// <summary>A deep cinematic impact with a brass-like swell ("braaam"): the title's entrance.</summary>
        Boom,
    }

    /// <summary>
    /// Makes every sound from sine tones, noise and envelopes (director 2026-11-14: sound made in code, no files,
    /// nothing to license). Pure C#: returns mono samples in −1..1 at <see cref="Rate"/>.
    /// </summary>
    public static class SoundSynth
    {
        public const int Rate = 44100;

        static double Note(int semitonesFromA4) => 440.0 * Math.Pow(2.0, semitonesFromA4 / 12.0);
        // Notes used below (equal temperament from A4 = 440 Hz).
        static readonly double C5 = Note(3), E5 = Note(7), G5 = Note(10), C6 = Note(15), E6 = Note(19), G6 = Note(22), A5 = Note(12);

        public static float[] Make(Sfx s)
        {
            switch (s)
            {
                case Sfx.Click: return Tone(0.045, 1250.0, 0.002, 0.04, 0.35, harmonics: 0.2);
                case Sfx.Open: return Mix(Tone(0.09, 660.0, 0.004, 0.08, 0.25), Delay(Tone(0.09, 990.0, 0.004, 0.08, 0.2), 0.03));
                case Sfx.Place: return Mix(Tone(0.16, 110.0, 0.002, 0.15, 0.6, sweepTo: 70.0), Noise(0.08, 0.002, 0.07, 0.25, smooth: 0.85));
                case Sfx.BuildComplete: return Arpeggio(new[] { C5, E5, G5, C6 }, 0.07, 0.35, 0.3);
                case Sfx.Correct: return Mix(Tone(0.35, C6, 0.005, 0.3, 0.32, harmonics: 0.15), Delay(Tone(0.45, G6, 0.005, 0.4, 0.3, harmonics: 0.15), 0.09));
                case Sfx.Wrong: return Mix(Tone(0.28, 220.0, 0.005, 0.25, 0.3, sweepTo: 150.0, harmonics: 0.5), Tone(0.28, 233.0, 0.005, 0.25, 0.2, sweepTo: 160.0));
                case Sfx.Beep: return Mix(Tone(0.08, 900.0, 0.003, 0.07, 0.25), Delay(Tone(0.1, 1300.0, 0.003, 0.09, 0.25), 0.1));
                case Sfx.Alert: return Mix(Mix(Tone(0.22, 620.0, 0.01, 0.2, 0.3, harmonics: 0.4), Delay(Tone(0.22, 465.0, 0.01, 0.2, 0.3, harmonics: 0.4), 0.24)),
                                           Delay(Mix(Tone(0.22, 620.0, 0.01, 0.2, 0.3, harmonics: 0.4), Delay(Tone(0.22, 465.0, 0.01, 0.2, 0.3, harmonics: 0.4), 0.24)), 0.5));
                case Sfx.Hover: return Mix(Tone(0.06, 1800.0, 0.003, 0.05, 0.10, harmonics: 0.1), Noise(0.05, 0.004, 0.04, 0.04, smooth: 0.6));
                case Sfx.Whoosh: return Riser(1.1, 0.35);
                case Sfx.Boom: return Braam();
                case Sfx.Milestone: return Mix(Mix(Tone(1.6, C5, 0.25, 1.3, 0.22), Tone(1.6, E5, 0.3, 1.25, 0.2)),
                                               Mix(Tone(1.6, G5, 0.35, 1.2, 0.2), Delay(Arpeggio(new[] { C6, E6, G6 }, 0.12, 0.6, 0.18), 0.35)));
                default: return Mix(Tone(0.5, A5, 0.01, 0.45, 0.22, vibrato: 6.0), Delay(Tone(0.6, E6, 0.01, 0.55, 0.2, vibrato: 6.0), 0.12));
            }
        }

        /// <summary>
        /// A quiet space ambience that loops without a seam: a low drone (every frequency fits a whole number of cycles
        /// in the loop), slow swells, and soft filtered "wind" cross-faded end into start.
        /// </summary>
        public static float[] Ambience(double seconds = 16.0, int seed = 7)
        {
            int n = (int)(seconds * Rate);
            int fade = Rate;   // 1 s cross-fade for the noise
            var wind = new double[n + fade];
            var rng = new Random(seed);
            double lp = 0.0, lp2 = 0.0;
            for (int i = 0; i < wind.Length; i++)
            {
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp += (white - lp) * 0.02;   // two one-pole low-passes: a soft rumble
                lp2 += (lp - lp2) * 0.05;
                wind[i] = lp2;
            }
            for (int i = 0; i < fade; i++)
            {
                double t = (double)i / fade;
                wind[i] = wind[i] * t + wind[n + i] * (1.0 - t);
            }
            var outp = new float[n];
            // Whole cycles per loop keep it seamless: f × seconds is an integer for each of these.
            double[] drone = { 55.0, 82.5, 110.0, 165.0 };
            double[] level = { 0.20, 0.12, 0.08, 0.04 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double swell = 0.65 + 0.35 * Math.Sin(2.0 * Math.PI * t / seconds);              // one swell per loop
                double shimmer = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * 3.0 * t / seconds + 1.0);
                double v = 0.0;
                for (int k = 0; k < drone.Length; k++)
                    v += level[k] * Math.Sin(2.0 * Math.PI * drone[k] * t) * (k >= 2 ? shimmer : swell);
                v += wind[i] * 2.5 * swell;
                outp[i] = (float)Math.Max(-1.0, Math.Min(1.0, v * 0.6));
            }
            return outp;
        }

        /// <summary>Filtered noise that swells and opens up, then cuts: a whoosh.</summary>
        static float[] Riser(double seconds, double amp)
        {
            int n = (int)(seconds * Rate);
            var s = new float[n];
            var rng = new Random(11);
            double y = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double smooth = 0.985 - 0.12 * t;   // brighter as it rises
                y = y * smooth + (rng.NextDouble() * 2.0 - 1.0) * (1.0 - smooth);
                double env = Math.Pow(t, 2.2) * (t > 0.92 ? (1.0 - t) / 0.08 : 1.0);
                s[i] = (float)Math.Max(-1.0, Math.Min(1.0, amp * env * y * 6.0));
            }
            return s;
        }

        /// <summary>
        /// The trailer impact: a sub-bass drop (70 to 28 Hz), a low noise blast, and a detuned brass-like chord on A1
        /// (55 Hz with fifths and octaves, rich in harmonics) that swells and fades over a few seconds, with an echo.
        /// </summary>
        static float[] Braam()
        {
            float[] sub = Tone(2.8, 70.0, 0.004, 2.2, 0.75, sweepTo: 28.0, harmonics: 0.3);
            float[] blast = Noise(1.6, 0.003, 0.9, 0.55, smooth: 0.975, seed: 21);
            float[] brass = new float[0];
            double[] notes = { 55.0, 55.6, 82.5, 110.0, 164.8 };
            double[] amps = { 0.20, 0.16, 0.14, 0.12, 0.07 };
            for (int k = 0; k < notes.Length; k++)
                brass = Mix(brass, Tone(4.0, notes[k], 0.06, 3.2, amps[k], harmonics: 1.0, vibrato: 3.0));
            float[] hit = Mix(Mix(sub, blast), brass);
            float[] echo = Delay(Scale(hit, 0.28), 0.42);
            return Mix(hit, echo);
        }

        static float[] Scale(float[] s, float k)
        {
            var o = new float[s.Length];
            for (int i = 0; i < s.Length; i++) o[i] = s[i] * k;
            return o;
        }

        // ---- building blocks ----

        /// <summary>A tone with a quick attack and an exponential-ish decay; optional pitch sweep, harmonics and vibrato.</summary>
        static float[] Tone(double seconds, double freq, double attack, double decay, double amp,
                            double sweepTo = 0.0, double harmonics = 0.0, double vibrato = 0.0)
        {
            int n = (int)(seconds * Rate);
            var s = new float[n];
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double f = sweepTo > 0.0 ? freq + (sweepTo - freq) * (t / seconds) : freq;
                if (vibrato > 0.0) f *= 1.0 + 0.006 * Math.Sin(2.0 * Math.PI * vibrato * t);
                phase += 2.0 * Math.PI * f / Rate;
                double env = t < attack ? t / attack : Math.Exp(-(t - attack) / Math.Max(1e-4, decay) * 3.0);
                double v = Math.Sin(phase) + harmonics * 0.5 * Math.Sin(2.0 * phase) + harmonics * 0.25 * Math.Sin(3.0 * phase);
                s[i] = (float)(amp * env * v / (1.0 + harmonics * 0.75));
            }
            return s;
        }

        static float[] Noise(double seconds, double attack, double decay, double amp, double smooth, int seed = 3)
        {
            int n = (int)(seconds * Rate);
            var s = new float[n];
            var rng = new Random(seed);
            double y = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                y = y * smooth + (rng.NextDouble() * 2.0 - 1.0) * (1.0 - smooth);
                double env = t < attack ? t / attack : Math.Exp(-(t - attack) / decay * 3.0);
                s[i] = (float)(amp * env * y * 3.0);
            }
            return s;
        }

        static float[] Arpeggio(double[] notes, double step, double tail, double amp)
        {
            float[] all = new float[0];
            for (int k = 0; k < notes.Length; k++)
                all = Mix(all, Delay(Tone(tail, notes[k], 0.004, tail * 0.8, amp, harmonics: 0.15), step * k));
            return all;
        }

        static float[] Delay(float[] s, double seconds)
        {
            int d = (int)(seconds * Rate);
            var o = new float[s.Length + d];
            Array.Copy(s, 0, o, d, s.Length);
            return o;
        }

        static float[] Mix(float[] a, float[] b)
        {
            var o = new float[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < o.Length; i++)
            {
                double v = (i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f);
                o[i] = (float)Math.Max(-1.0, Math.Min(1.0, v));
            }
            return o;
        }
    }
}
