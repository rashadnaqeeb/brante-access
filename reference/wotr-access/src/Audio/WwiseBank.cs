using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Generates a Wwise v135 soundbank from WAV files at runtime — no Wwise authoring tool, which
    /// keeps the drop-a-wav-into-assets/audio workflow alive. The binary layout was learned
    /// byte-for-byte from the game's own banks (Wrath_Main_Default): BKHD v135; DIDX/DATA holding
    /// PCM .wem media (RIFF, fmt 0xFFFE/24); HIRC with one Attenuation (the game's rich 75m falloff
    /// curve, copied verbatim and re-ID'd) and, per wav, a Sound (the game's minimal 48-byte shape +
    /// 3D listener-relative positioning + the attenuation referenced as prop 70, a uint bit-cast
    /// into the float prop bundle), a Play Action, and an Event whose id is the Wwise FNV-1 hash of
    /// "wa_&lt;stem&gt;" — so <c>AkSoundEngine.PostEvent("wa_&lt;stem&gt;", emitter)</c> just works.
    /// Sounds route to bus 1088520938 (a real Init.bnk bus the game's SFX mixers use), so the game's
    /// volume sliders and mixer states apply to ours.
    /// </summary>
    internal static class WwiseBank
    {
        private const uint BankVersion = 135;
        private const uint BusId = 247557814;        // the bus the game's 3D SFX mixer routes to (Init.bnk)
        private const uint AttenuationId = 1001207880; // the game's own rich attenuation (kept under its original id)

        // The game's attenuation object payload (id field excluded), captured from Wrath_Main_Default
        // object 1001207880: volume/LPF/HPF/spread curves out to 75m.
        private const string AttenuationB64 =
            "AAABAgMEBf8GAgYAAAAAAAAAAAAJAAAAbxKDOgAAAAAJAAAAAADAQAAAAAAHAAAAAABgQWNk/74CAAAAAACQQT5tV78BAAAAAADI" +
            "QQAAgL8EAAAAAgYAAAAAAJ+bUr4JAAAAAAAAQJ+bUr4BAAAAAADAQNS33r0HAAAAAABgQSGIlb4EAAAAAACQQR8v/74EAAAAAADI" +
            "QQAAgL8EAAAAAgYAAAAAAJ+bUr4JAAAAAAAAQJ+bUr4BAAAAAADAQNS33r0HAAAAAABgQSGIlb4EAAAAAACQQWNk/74EAAAAAADI" +
            "QQAAgL8EAAAAAAQAAAAAAAAAAAAJAAAAAAAAQQAAAAAGAAAAAABgQQAAoEECAAAAAADIQQAAIEIEAAAAAAQAAAAAAAAAAAAJAAAA" +
            "AAAAQQAAAAAFAAAAAABwQQAAoEEEAAAAAADIQQAA8EEEAAAAAAQAAAAAAAAAlkIBAAAAAACAQAAASEIEAAAAAAAgQQAAIEIEAAAA" +
            "AADIQQAAoEEEAAAAAAA=";

        /// <summary>Wwise FNV-1 32-bit hash of a (lowercased) name — what PostEvent(string) computes.</summary>
        public static uint Fnv32(string name)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(name.ToLowerInvariant()))
            {
                h = unchecked(h * 16777619);
                h ^= b;
            }
            return h;
        }

        private static uint Fnv30(string name) => Fnv32(name) & 0x3FFFFFFF;

        /// <summary>Build the bank from (stem, wav-bytes) pairs. Event name per wav: "wa_&lt;stem&gt;".
        /// Stems in <paramref name="loopStems"/> get an infinite LOOP (prop 58 = 0, as the game's
        /// ambience sounds encode it) — stop the voice via its playing id.</summary>
        public static byte[] Build(IEnumerable<KeyValuePair<string, byte[]>> wavs, string bankName,
            ICollection<string> loopStems, out uint bankId)
        {
            uint bid = bankId = Fnv32(bankName); // local copy: out params can't be captured in lambdas
            uint mixerId = Fnv32(bankName + "_mixer");
            var atten = Convert.FromBase64String(AttenuationB64);

            // The game wires 3D HIERARCHICALLY: a root ActorMixer carries the positioning + the
            // attenuation (prop 70) and the child sounds inherit (positioning bits 0) — grafting the
            // 3D flags directly onto leaf sounds is ignored. So we replicate the game's working trio
            // byte-for-byte: its attenuation (original id — harmless duplicate if the game bank is
            // loaded), a re-id'd copy of its 3D mixer with OUR sounds as the children, and per wav a
            // clone of its child-sound shape.
            var media = new List<KeyValuePair<uint, byte[]>>();
            var hirc = new List<byte[]> { HircObj(14, AttenuationId, atten) };
            var soundIds = new List<uint>();
            var tail = new List<byte[]>();

            foreach (var kv in wavs)
            {
                string stem = kv.Key.ToLowerInvariant();
                byte[] wem;
                try { wem = PcmWem(kv.Value); }
                catch (Exception e) { Main.Log?.Warning("[wwise] skipping wav '" + stem + "': " + e.Message); continue; }
                uint srcId = Fnv30("wa_media_" + stem);
                uint sndId = Fnv30("wa_sound_" + stem);
                uint actId = Fnv30("wa_action_" + stem);
                uint evtId = Fnv32("wa_" + stem);
                media.Add(new KeyValuePair<uint, byte[]>(srcId, wem));
                soundIds.Add(sndId);
                bool loop = loopStems != null && loopStems.Contains(stem);
                hirc.Add(HircObj(2, sndId, SoundPayload(srcId, (uint)wem.Length, mixerId, loop)));
                tail.Add(HircObj(3, actId, ActionPlayPayload(sndId, bid)));
                tail.Add(HircObj(4, evtId, EventPayload(actId)));
            }
            hirc.Add(HircObj(7, mixerId, MixerPayload(soundIds)));
            hirc.AddRange(tail);

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                // DIDX + DATA (16-byte aligned, like the game's banks)
                var didx = new MemoryStream();
                var data = new MemoryStream();
                using (var dw = new BinaryWriter(didx))
                {
                    foreach (var m in media)
                    {
                        while (data.Length % 16 != 0) data.WriteByte(0);
                        dw.Write(m.Key); dw.Write((uint)data.Length); dw.Write((uint)m.Value.Length);
                        data.Write(m.Value, 0, m.Value.Length);
                    }

                    Section(w, "BKHD", bw =>
                    {
                        bw.Write(BankVersion); bw.Write(bid); bw.Write(0u); bw.Write(16u); bw.Write(0u);
                    });
                    var didxBytes = didx.ToArray();
                    var dataBytes = data.ToArray();
                    w.Write(Encoding.ASCII.GetBytes("DIDX")); w.Write((uint)didxBytes.Length); w.Write(didxBytes);
                    w.Write(Encoding.ASCII.GetBytes("DATA")); w.Write((uint)dataBytes.Length); w.Write(dataBytes);
                    Section(w, "HIRC", bw =>
                    {
                        bw.Write((uint)hirc.Count);
                        foreach (var o in hirc) bw.Write(o);
                    });
                }
                return ms.ToArray();
            }
        }

        private static void Section(BinaryWriter w, string tag, Action<BinaryWriter> body)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                body(bw);
                var bytes = ms.ToArray();
                w.Write(Encoding.ASCII.GetBytes(tag));
                w.Write((uint)bytes.Length);
                w.Write(bytes);
            }
        }

        private static byte[] HircObj(byte type, uint id, byte[] payload)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(type);
                w.Write((uint)(4 + payload.Length));
                w.Write(id);
                w.Write(payload);
                return ms.ToArray();
            }
        }

        // Byte-faithful clone of the game's 3D mixer child sound (Wrath_Main_Default 826933298):
        // inherit everything from the parent mixer (positioning bits 0), neutral gain/pitch.
        private static byte[] SoundPayload(uint sourceId, uint mediaSize, uint parentMixerId, bool loop)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(0x00010001u);    // plugin: PCM (the template uses Vorbis; codec is per-source)
                w.Write((byte)0);        // stream type: in-bank
                w.Write(sourceId); w.Write(mediaSize);
                w.Write((byte)0);        // source bits
                w.Write((byte)0); w.Write((byte)0);   // fx override, num fx
                w.Write((byte)0);        // override attachment params
                w.Write(0u);             // override bus: none — routed via the parent mixer
                w.Write(parentMixerId);  // direct parent: our copy of the game's 3D mixer
                w.Write((byte)0);        // byBitVector
                if (loop)
                {
                    // props sorted ascending: 6 (gain) then 58 (loop; the int count bit-cast — 0 = forever)
                    w.Write((byte)2); w.Write((byte)6); w.Write((byte)58); w.Write(0f); w.Write(0u);
                }
                else
                {
                    w.Write((byte)1); w.Write((byte)6); w.Write(0f);          // prop 6 (gain): neutral
                }
                w.Write((byte)1); w.Write((byte)2); w.Write(0f); w.Write(0f); // ranged prop 2 (pitch): none
                w.Write((byte)0x00);     // positioning: INHERIT the mixer 3D config
                w.Write((byte)0);        // aux bits
                w.Write(0u);             // reflections aux bus
                w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((byte)0); w.Write((byte)0); // adv settings
                w.Write((byte)0); w.Write((byte)0); // state props/groups
                w.Write((ushort)0);      // rtpc count
                return ms.ToArray();
            }
        }

        // Byte-faithful clone of the game root 3D ActorMixer (Wrath_Main_Default 422171592):
        // bus 247557814, props {6: +2dB, 23: 6, 70: the attenuation}, positioning 0x03/0x0A, and the
        // exact aux/advanced tail it ships with — only the id and the children list are ours.
        private static byte[] MixerPayload(List<uint> childSoundIds)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)0); w.Write((byte)0);   // fx override, num fx
                w.Write((byte)0);        // override attachment params
                w.Write(BusId);          // the game 3D SFX bus
                w.Write(0u);             // no parent (root)
                w.Write((byte)0);        // byBitVector
                w.Write((byte)3);        // 3 props
                w.Write((byte)6); w.Write((byte)23); w.Write((byte)70);
                w.Write(2.0f);           // prop 6 (verbatim from the game mixer)
                w.Write(6.0f);           // prop 23 (verbatim)
                w.Write(AttenuationId);  // prop 70: attenuation (uint bit-cast)
                w.Write((byte)0);        // ranged props
                w.Write((byte)0x03);     // positioning bits (verbatim)
                w.Write((byte)0x0A);     // bits3d (verbatim)
                w.Write((byte)0x02);     // aux bits (verbatim)
                w.Write(0u);             // reflections aux bus
                w.Write((byte)0); w.Write((byte)1); w.Write((ushort)0); w.Write((byte)0); w.Write((byte)0); // adv (verbatim)
                w.Write((byte)0); w.Write((byte)0); // state props/groups
                w.Write((ushort)0);      // rtpc count
                w.Write((uint)childSoundIds.Count);
                foreach (var id in childSoundIds) w.Write(id);
                return ms.ToArray();
            }
        }

        private static byte[] ActionPlayPayload(uint targetId, uint bankId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((ushort)0x0403); // Play
                w.Write(targetId);
                w.Write((byte)0);        // idExt bit
                w.Write((byte)0); w.Write((byte)0); // prop bundles
                w.Write((byte)0x04);     // fade curve (the game's value)
                w.Write(bankId);
                return ms.ToArray();
            }
        }

        private static byte[] EventPayload(uint actionId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)1);
                w.Write(actionId);
                return ms.ToArray();
            }
        }

        // ---- WAV → PCM wem (RIFF/WAVE, fmt 0xFFFE size 24, 16-bit) ----

        private static byte[] PcmWem(byte[] wav)
        {
            ParseWav(wav, out int channels, out int rate, out int bits, out bool isFloat, out byte[] frames);
            byte[] pcm16 = To16Bit(frames, bits, isFloat);
            int block = channels * 2;
            uint channelMask = channels == 1 ? 0x4u : 0x3u;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                using (var body = new MemoryStream())
                using (var bw = new BinaryWriter(body))
                {
                    bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                    bw.Write(Encoding.ASCII.GetBytes("fmt "));
                    bw.Write(24u);
                    bw.Write((ushort)0xFFFE); bw.Write((ushort)channels); bw.Write((uint)rate);
                    bw.Write((uint)(rate * block)); bw.Write((ushort)block); bw.Write((ushort)16);
                    bw.Write((ushort)6); bw.Write((ushort)0); bw.Write(channelMask);
                    bw.Write(Encoding.ASCII.GetBytes("data"));
                    bw.Write((uint)pcm16.Length);
                    bw.Write(pcm16);
                    var bytes = body.ToArray();
                    w.Write(Encoding.ASCII.GetBytes("RIFF"));
                    w.Write((uint)bytes.Length);
                    w.Write(bytes);
                }
                return ms.ToArray();
            }
        }

        private static void ParseWav(byte[] wav, out int channels, out int rate, out int bits, out bool isFloat, out byte[] frames)
        {
            if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
                throw new InvalidDataException("not a RIFF wav");
            channels = 0; rate = 0; bits = 0; isFloat = false; frames = null;
            int off = 12;
            while (off + 8 <= wav.Length)
            {
                string tag = Encoding.ASCII.GetString(wav, off, 4);
                int size = BitConverter.ToInt32(wav, off + 4);
                if (tag == "fmt ")
                {
                    int fmtTag = BitConverter.ToUInt16(wav, off + 8);
                    channels = BitConverter.ToUInt16(wav, off + 10);
                    rate = BitConverter.ToInt32(wav, off + 12);
                    bits = BitConverter.ToUInt16(wav, off + 22);
                    isFloat = fmtTag == 3 || (fmtTag == 0xFFFE && size >= 40 && BitConverter.ToUInt16(wav, off + 32) == 3);
                }
                else if (tag == "data")
                {
                    int n = Math.Min(size, wav.Length - off - 8);
                    frames = new byte[n];
                    Buffer.BlockCopy(wav, off + 8, frames, 0, n);
                }
                off += 8 + size + (size & 1);
            }
            if (frames == null || channels == 0) throw new InvalidDataException("missing fmt/data");
        }

        private static byte[] To16Bit(byte[] frames, int bits, bool isFloat)
        {
            if (bits == 16 && !isFloat) return frames;
            int bytesPer = bits / 8;
            int samples = frames.Length / bytesPer;
            var outBytes = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                int v;
                if (isFloat && bits == 32)
                {
                    float f = BitConverter.ToSingle(frames, i * 4);
                    v = (int)(UnityEngine.Mathf.Clamp(f, -1f, 1f) * 32767f);
                }
                else if (bits == 8) v = (frames[i] - 128) << 8;
                else if (bits == 24)
                {
                    v = frames[i * 3] | (frames[i * 3 + 1] << 8) | (frames[i * 3 + 2] << 16);
                    if ((v & 0x800000) != 0) v -= 0x1000000;
                    v >>= 8;
                }
                else if (bits == 32) v = BitConverter.ToInt32(frames, i * 4) >> 16;
                else throw new InvalidDataException("unsupported bit depth " + bits);
                outBytes[i * 2] = (byte)(v & 0xFF);
                outBytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            return outBytes;
        }
    }
}
