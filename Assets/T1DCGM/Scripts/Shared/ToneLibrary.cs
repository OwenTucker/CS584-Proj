using UnityEngine;

namespace T1DCGM
{
    /// <summary>Generates simple feedback sounds at runtime so the project needs no audio assets.</summary>
    public static class ToneLibrary
    {
        const int k_SampleRate = 44100;
        static AudioClip s_Success, s_Error, s_Finish, s_Click;

        public static AudioClip Success() => s_Success ??= Notes("Success", 0.09f, 0.25f, 660f, 880f);
        public static AudioClip Error() => s_Error ??= Notes("Error", 0.14f, 0.3f, 220f, 180f);
        public static AudioClip Finish() => s_Finish ??= Notes("Finish", 0.12f, 0.25f, 523f, 659f, 784f, 1047f);
        public static AudioClip Click() => s_Click ??= Notes("Click", 0.025f, 0.35f, 1800f);

        static AudioClip Notes(string name, float noteLength, float volume, params float[] frequencies)
        {
            int perNote = Mathf.CeilToInt(noteLength * k_SampleRate);
            var data = new float[perNote * frequencies.Length];
            for (int n = 0; n < frequencies.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    float t = (float)i / k_SampleRate;
                    float envelope = Mathf.Min(1f, i / 200f) * (1f - (float)i / perNote);
                    data[n * perNote + i] = Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) * envelope * volume;
                }
            }

            var clip = AudioClip.Create(name, data.Length, 1, k_SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
