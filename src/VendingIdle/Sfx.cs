using System;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;

namespace VendingIdle;

/// <summary>
/// Every sound the game makes. Three cues, all tied to something the player did
/// themselves -- customers buying in the background stay silent, because a sound
/// per idle tick becomes a drone within a minute of hiring the second customer.
///
/// Nothing in here is allowed to take the game down. A machine with no audio
/// device (a CI box, a headless screenshot run) throws from the audio subsystem,
/// so a failure mutes the game for the rest of the session rather than
/// propagating -- silence is a far better outcome than a crash on a cosmetic.
/// </summary>
public sealed class Sfx
{
    /// <summary>Master level. UI cues sit well under the mix so they never startle.</summary>
    private const float ShakeVolume = 0.55f;
    private const float DeniedVolume = 0.40f;
    private const float PurchaseVolume = 0.50f;
    private const float BottleVolume = 0.38f;

    /// <summary>
    /// Clinks allowed per frame. A late-game shake empties two dozen slots at
    /// once and their bottles land within a few frames of each other; unthrottled
    /// that is two dozen overlapping samples, which is noise rather than feedback.
    /// The first is loudest and each one after it ducks, so a big shake reads as
    /// one fat clatter instead of a wall.
    /// </summary>
    private const int BottlesPerFrame = 3;

    private readonly Random _rng = new();

    private SoundEffect? _shake;
    private SoundEffect? _denied;
    private SoundEffect? _purchase;
    private SoundEffect? _bottle;

    private int _bottleBudget;

    /// <summary>False once muted by request, or after the audio device has failed.</summary>
    public bool Enabled { get; private set; } = true;

    public void Mute() => Enabled = false;

    public void Load(ContentManager content)
    {
        if (!Enabled) return;

        try
        {
            _shake = content.Load<SoundEffect>("Audio/shake");
            _denied = content.Load<SoundEffect>("Audio/denied");
            _purchase = content.Load<SoundEffect>("Audio/purchase");
            _bottle = content.Load<SoundEffect>("Audio/bottle");
        }
        catch (Exception e) when (e is NoAudioHardwareException or ContentLoadException)
        {
            Enabled = false;
        }
    }

    /// <summary>
    /// The machine being rattled. Pitch wanders a little on every shake: an idle
    /// game is played by clicking the same button hundreds of times, and the
    /// identical sample on every one of them is what turns a good cue into a
    /// machine-gun. Spare-change shakes are quieter -- nothing came out.
    /// </summary>
    public void Shake(bool paidOut) =>
        Play(_shake, paidOut ? ShakeVolume : ShakeVolume * 0.6f, Vary(0.12f));

    /// <summary>Asked for something they cannot afford.</summary>
    public void Denied() => Play(_denied, DeniedVolume, 0f);

    /// <summary>A purchase went through: a slot, an upgrade, a restock.</summary>
    public void Purchase() => Play(_purchase, PurchaseVolume, Vary(0.06f));

    /// <summary>Resets the per-frame clink budget. Call once per update.</summary>
    public void BeginFrame() => _bottleBudget = BottlesPerFrame;

    /// <summary>
    /// A bottle hitting the tray. <paramref name="pitch"/> is the drink's own
    /// voice (see <c>DrinkDef.SoundPitch</c>), nudged a little each time so a
    /// slot emptying bottle after bottle does not tick like a metronome.
    /// </summary>
    public void Bottle(float pitch)
    {
        if (_bottleBudget <= 0) return;

        // Ducks toward the back of the frame's budget: 1.0, then 0.66, then 0.33.
        var duck = _bottleBudget / (float)BottlesPerFrame;
        _bottleBudget--;

        Play(_bottle, BottleVolume * duck, Math.Clamp(pitch + Vary(0.07f), -1f, 1f));
    }

    private float Vary(float spread) => (float)((_rng.NextDouble() * 2.0 - 1.0) * spread);

    private void Play(SoundEffect? effect, float volume, float pitch)
    {
        if (!Enabled || effect is null) return;

        try
        {
            effect.Play(volume, pitch, 0f);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InstancePlayLimitException)
        {
            // Out of voices is transient and harmless; a missing device is not,
            // but neither is worth a crash. Stop trying either way.
            Enabled = false;
        }
    }
}
