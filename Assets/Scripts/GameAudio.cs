using UnityEngine;

/// <summary>
/// One place for all the game's sounds. Each group picks a random clip, never the same
/// one twice in a row, with slight pitch variation so repeated sounds (chewing especially)
/// don't turn robotic.
///
/// Put this on an empty GameObject. Wire the public Play methods to the UnityEvents on
/// SandwichEater and SeagullSystem in the Inspector. The intro and the music track play
/// themselves on start -- they need no wiring.
/// </summary>
public class GameAudio : MonoBehaviour
{
    [System.Serializable]
    public class SoundBank
    {
        [Tooltip("Drop every clip in this group here. One is picked at random each time.")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch range. Set both to 1 for no variation.")]
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

        private int lastIndex = -1;

        public AudioClip Next()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];

            // Pick from everything except the clip we played last time.
            int index = Random.Range(0, clips.Length - 1);
            if (index >= lastIndex) index++;
            if (index >= clips.Length) index = 0;

            lastIndex = index;
            return clips[index];
        }

        public float RandomPitch()
        {
            return Random.Range(Mathf.Min(pitchRange.x, pitchRange.y),
                                Mathf.Max(pitchRange.x, pitchRange.y));
        }
    }

    [Header("Output")]
    [Tooltip("Leave empty and one is added automatically.")]
    public AudioSource source;
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("Sound Groups")]
    public SoundBank bite = new SoundBank();
    public SoundBank chew = new SoundBank();
    public SoundBank gulp = new SoundBank();
    public SoundBank punch = new SoundBank();
    public SoundBank win = new SoundBank();
    public SoundBank lose = new SoundBank();
    public SoundBank intro = new SoundBank();

    [Header("Intro")]
    [Tooltip("Plays one intro clip when the scene starts. No event wiring needed.")]
    public bool playIntroOnStart = true;
    [Tooltip("Seconds to wait before the intro plays.")]
    public float introDelay = 0f;

    [Header("Background Music")]
    public AudioClip musicClip;
    public bool playMusicOnStart = true;
    [Range(0f, 1f)] public float musicVolume = 0.35f;

    private AudioSource musicSource;

    private void Awake()
    {
        if (source == null) source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D -- these are feedback sounds, not world sounds
    }

    private void Start()
    {
        if (playIntroOnStart)
        {
            if (introDelay > 0f) Invoke(nameof(PlayIntro), introDelay);
            else PlayIntro();
        }

        if (playMusicOnStart && musicClip != null) StartMusic();
    }

    // ---- Hook these to UnityEvents in the Inspector ----

    public void PlayBite() => Play(bite);
    public void PlayChew() => Play(chew);
    public void PlayGulp() => Play(gulp);
    public void PlayPunch() => Play(punch);
    public void PlayWin() => Play(win);
    public void PlayLose() => Play(lose);
    public void PlayIntro() => Play(intro);

    // ---- Music ----

    public void StartMusic()
    {
        if (musicClip == null) return;

        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.loop = true;
        }

        musicSource.clip = musicClip;
        musicSource.volume = musicVolume * masterVolume;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
    }

    // ---- Internals ----

    private void Play(SoundBank bank)
    {
        if (bank == null || source == null) return;

        AudioClip clip = bank.Next();
        if (clip == null) return;

        source.pitch = bank.RandomPitch();
        source.PlayOneShot(clip, bank.volume * masterVolume);
    }
}