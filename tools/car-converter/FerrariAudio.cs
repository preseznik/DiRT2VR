using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DiRT2VR.CustomTracks;

namespace DiRT2VR.CustomCars;

internal static class FerrariAudio
{
    sealed record Sample(string Name, int Rate, int Channels, int Frames, byte[] Pcm);
    sealed record Band(double Centre, string Name, double CycleHz);
    sealed record NativeSample(string Name, int Rate, byte[] Pcm);
    const int Rate = 32000, Frames = Rate * 10;
    const double Minimum = 4000.0 / 18800;
    // Blend positions are not the pitch at which the source was recorded.
    // Calibrate each pinned recording's engine-cycle frequency separately. A
    // four-stroke cycle is two crank revolutions; the target is RPM / 120.
    static readonly Dictionary<string, Band[]> Recipes = new()
    {
        ["up_en"] = [new(Minimum,"F2004_in_idle",29.1),new(.25,"F2004_in_on_low",65.8),new(.50,"F2004_in_on_mid",96.1),new(.75,"F2004_in_on_high_mix",135.1),new(1,"F2004_in_on_veryhigh_mix",151.8)],
        ["up_ex"] = [new(Minimum,"F2004_ex_idle",26.5),new(.25,"F2004_ex_on_low_backg",28.6),new(.50,"F2004_ex_on_mid_exh",108.6),new(.75,"F2004_ex_on_high_exh",105.7),new(1,"F2004_ex_on_veryhigh_exh",105.2)],
        ["dn_en"] = [new(Minimum,"F2004_in_idle",29.1),new(.30,"F2004_in_off_low",69.1),new(.65,"F2004_in_off_midhigh_mix",116.6),new(1,"F2004_in_off_high",125.4)],
        ["dn_ex"] = [new(Minimum,"F2004_ex_idle",26.5),new(.30,"F2004_ex_off_low",73.4),new(.65,"F2004_ex_off_mid",111.6),new(1,"F2004_ex_off_high",111.6)]
    };
    static Dictionary<string, Sample> ReadBank(string path)
    {
        byte[] data = File.ReadAllBytes(path); int offset = data.AsSpan().IndexOf("FSB5"u8);
        if (offset < 0 || offset + 60 > data.Length) throw new IOException("Missing F2004 PCM bank.");
        using var stream = new MemoryStream(data, false); stream.Position = offset + 4; using var reader = new BinaryReader(stream);
        uint version = reader.ReadUInt32(), count = reader.ReadUInt32(), headers = reader.ReadUInt32(), names = reader.ReadUInt32(), size = reader.ReadUInt32(), mode = reader.ReadUInt32();
        if (version != 1 || count != 46 || mode != 2 || (long)offset + 60 + headers + names + size > data.Length) throw new IOException("Unsupported F2004 audio format.");
        long namesStart = offset + 60L + headers, payload = namesStart + names;
        stream.Position = offset + 60; var result = new Dictionary<string, Sample>(StringComparer.Ordinal); int previousEnd = 0;
        int[] frequencies = [0,8000,11000,11025,16000,22050,24000,32000,44100,48000];
        for (int i = 0; i < count; i++)
        {
            ulong raw = reader.ReadUInt64(); bool more = (raw & 1) != 0; int frequencyId = (int)((raw >> 1) & 15);
            int rate = frequencyId < frequencies.Length ? frequencies[frequencyId] : 0, channels = (int)((raw >> 5) & 1) + 1;
            int start = checked((int)((raw >> 6) & 0xfffffff) * 16), frames = checked((int)((raw >> 34) & 0x3fffffff));
            while (more)
            {
                uint chunk = reader.ReadUInt32(); more = (chunk & 1) != 0; int length = (int)((chunk >> 1) & 0xffffff), kind = (int)(chunk >> 25);
                if (stream.Position + length > namesStart) throw new IOException("Invalid audio metadata length.");
                byte[] extra = reader.ReadBytes(length);
                if (kind == 1) { if (length != 1) throw new IOException("Invalid audio channel metadata."); channels = extra[0]; }
                if (kind == 2) { if (length != 4) throw new IOException("Invalid audio rate metadata."); rate = BitConverter.ToInt32(extra); }
            }
            long resume = stream.Position; stream.Position = namesStart + i * 4; uint nameOffset = reader.ReadUInt32();
            if (nameOffset < count * 4 || nameOffset >= names) throw new IOException("Invalid audio name offset.");
            int nameStart = checked((int)(namesStart + nameOffset)), nameEnd = Array.IndexOf(data, (byte)0, nameStart, checked((int)(payload - nameStart)));
            if (nameEnd < 0) throw new IOException("Unterminated audio sample name.");
            string name = new UTF8Encoding(false, true).GetString(data, nameStart, nameEnd - nameStart);
            int bytes = checked(frames * channels * 2);
            if (rate is < 8000 or > 192000 || channels is < 1 or > 8 || start < previousEnd || (long)start + bytes > size) throw new IOException("Invalid PCM sample range.");
            byte[] pcm = data.AsSpan(checked((int)payload + start), bytes).ToArray(); previousEnd = start + bytes;
            if (!result.TryAdd(name, new(name, rate, channels, frames, pcm))) throw new IOException("Duplicate audio sample name.");
            stream.Position = resume;
        }
        if (stream.Position != namesStart) throw new IOException("Audio headers were not fully consumed.");
        return result;
    }
    internal static void Build(string source, string donor, string output, CancellationToken cancel)
    {
        var bank = ReadBank(SafeFiles.Inside(source, "sfx/ks_ferrari_f2004.bank"));
        var used = new HashSet<string>(StringComparer.Ordinal);
        double[] Mono(string name, int targetRate)
        {
            cancel.ThrowIfCancellationRequested(); var sample = bank[name]; used.Add(name);
            int length = Math.Min(sample.Frames, sample.Rate * 12); var mono = new double[length];
            for (int i = 0; i < length; i++)
            {
                double sum = 0; for (int c = 0; c < sample.Channels; c++) sum += BitConverter.ToInt16(sample.Pcm, (i * sample.Channels + c) * 2);
                mono[i] = sum / sample.Channels;
            }
            if (length < 2) throw new IOException("Empty engine recording.");
            var result = new double[checked((int)Math.Round((double)length * targetRate / sample.Rate))];
            for (int i = 0; i < result.Length; i++) { double p = (double)i * sample.Rate / targetRate; int low = Math.Min((int)p, length - 1); double f = p - (int)p; result[i] = mono[low] * (1 - f) + mono[Math.Min(low + 1, length - 1)] * f; }
            return result;
        }
        static byte[] Pcm(IEnumerable<double> values)
        {
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            foreach (double value in values) writer.Write((short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue)); return output.ToArray();
        }
        byte[] Sweep(string role)
        {
            var bands = Recipes[role]; var samples = bands.Select(b => Mono(b.Name, Rate)).ToArray(); var positions = new double[bands.Length]; var result = new double[Frames];
            for (int i = 0; i < Frames; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                double t = (double)i / (Frames - 1), rev = Minimum + (1 - Minimum) * (role.StartsWith("dn", StringComparison.Ordinal) ? 1 - t : t);
                int high = Array.FindIndex(bands, b => b.Centre >= rev); if (high < 0) high = bands.Length - 1; int low = Math.Max(0, high - 1);
                double blend = high == low ? 0 : Math.Clamp((rev - bands[low].Centre) / (bands[high].Centre - bands[low].Centre), 0, 1); blend = blend * blend * (3 - 2 * blend);
                double Value(int j) { var data = samples[j]; double p = positions[j] % data.Length; int a = (int)p; double f = p - a; return data[a] * (1 - f) + data[(a + 1) % data.Length] * f; }
                result[i] = low == high ? Value(low) : Value(low) * (1 - blend) + Value(high) * blend;
                for (int j = 0; j < bands.Length; j++) positions[j] += rev * (18800.0 / 120) / bands[j].CycleHz;
            }
            return Pcm(result);
        }
        string folder = SafeFiles.Inside(output, "audio/dictionaries/d2vr_f04"); Directory.CreateDirectory(folder);
        var packages = new List<(string Name, NativeSample[] Samples, byte[] Table)>();
        void Package(string name, NativeSample[] samples)
        {
            cancel.ThrowIfCancellationRequested(); using var data = new MemoryStream(); using var dw = new BinaryWriter(data); using var table = new MemoryStream(); using var tw = new BinaryWriter(table);
            tw.Write(0); foreach (var sample in samples)
            {
                if (sample.Name.Length > 15 || sample.Pcm.Length % 2 != 0) throw new IOException("Unsupported native sample identity.");
                dw.Write(uint.MaxValue); dw.Write(sample.Pcm); tw.Write((ushort)sample.Rate); tw.Write((ushort)0); tw.Write(FixedName(sample.Name)); tw.Write(checked((uint)data.Length));
            }
            tw.Write("WIP\0"u8); File.WriteAllBytes(Path.Combine(folder, name + ".WIP"), data.ToArray()); packages.Add((name, samples, table.ToArray()));
        }
        Package("eng_f04", new[] { "dn_en", "dn_ex", "up_en", "up_ex" }.Select(role => new NativeSample(role, Rate, Sweep(role))).ToArray());
        Package("mec_f04_shift", new[] { ("mec_gearchang01", "gearup"), ("mec_gearchang02", "gearupEXT"), ("mec_gearchang03", "geardnEXT") }.Select(p => new NativeSample(p.Item1, 48000, Pcm(Mono(p.Item2, 48000)))).ToArray());
        byte[] stock = File.ReadAllBytes(SafeFiles.Inside(donor, "audio/dictionaries/dd_mec/NESSamples.dic"));
        var exhaust = new List<string>(); int stockCount = BitConverter.ToInt32(stock, 12);
        if (stockCount is < 1 or > 4096) throw new IOException("Unsupported mechanical dictionary.");
        for (int i = 0; i < stockCount; i++)
        {
            int position = 16 + i * 24; if (position + 24 > stock.Length) throw new IOException("Truncated mechanical dictionary.");
            if (Encoding.ASCII.GetString(stock, position + 8, 16).TrimEnd('\0') != "mec_exhdeton08") continue;
            int start = BitConverter.ToInt32(stock, position), count = BitConverter.ToInt32(stock, position + 4);
            if (count != 47 || start < 0 || (long)start + 4 + 24 * count > stock.Length) throw new IOException("Unsupported exhaust dictionary.");
            for (int j = 0; j < count; j++) exhaust.Add(Encoding.ASCII.GetString(stock, start + 4 + j * 24 + 4, 16).TrimEnd('\0'));
        }
        if (exhaust.Count != 47) throw new IOException("Missing native exhaust samples.");
        var backfires = new[] { 1, 3, 4, 5 }.Select(i => Pcm(Mono("fa01_backfire_" + i, 48000))).ToArray();
        Package("mec_f04_exh", exhaust.Select((name, i) => new NativeSample(name, 48000, backfires[i % backfires.Length])).ToArray());
        using var dictionary = new MemoryStream(); using var writer = new BinaryWriter(dictionary);
        writer.Write("DIC1"u8); writer.Write(0x0054f46f); writer.Write("2.00"u8); writer.Write(packages.Count);
        int tableOffset = 16 + 24 * packages.Count;
        foreach (var package in packages) { writer.Write(tableOffset); writer.Write(package.Samples.Length); writer.Write(FixedName(package.Name)); tableOffset += package.Table.Length; }
        foreach (var package in packages) writer.Write(package.Table);
        byte[] dic = dictionary.ToArray(); File.WriteAllBytes(Path.Combine(folder, "NESSamples.dic"), dic);
        // Verify every table offset, sample end and emitted PCM payload.
        for (int i = 0; i < packages.Count; i++)
        {
            int start = BitConverter.ToInt32(dic, 16 + i * 24), previous = 0; var package = packages[i]; byte[] data = File.ReadAllBytes(Path.Combine(folder, package.Name + ".WIP"));
            foreach (var (sample, index) in package.Samples.Select((s, j) => (s, j)))
            {
                int end = BitConverter.ToInt32(dic, start + 4 + index * 24 + 20);
                if (end != previous + 4 + sample.Pcm.Length || BitConverter.ToUInt32(data, previous) != uint.MaxValue || !data.AsSpan(previous + 4, sample.Pcm.Length).SequenceEqual(sample.Pcm)) throw new IOException("Native audio dictionary readback failed."); previous = end;
            }
            if (previous != data.Length) throw new IOException("Native audio payload not fully consumed.");
        }
        WriteEngine(SafeFiles.Inside(donor, "audio/cars/tstengine.ini"), SafeFiles.Inside(output, CarPack.EngineIni));
        SafeFiles.WriteJson(SafeFiles.Inside(output, CarPack.Support + "/audio-build.json"), new { Recipes, SourceBankSha256 = SafeFiles.Hash(SafeFiles.Inside(source, "sfx/ks_ferrari_f2004.bank")), Samples = used.Order().Select(name => new { Name = name, bank[name].Rate, bank[name].Channels, PcmSha256 = Convert.ToHexString(SHA256.HashData(bank[name].Pcm)) }), PlaybackCalibrationAccepted = false });
    }
    static byte[] FixedName(string name) { byte[] result = new byte[16]; if (Encoding.ASCII.GetBytes(name, result) > 15) throw new IOException("Native audio name too long."); return result; }
    static void WriteEngine(string source, string output)
    {
        var lines = new List<string>(); string section = ""; bool skip = false;
        foreach (string original in File.ReadLines(source))
        {
            string line = original.Trim(); if (line.StartsWith('[')) { section = line.Trim('[', ']'); skip = section is "Loop" or "End"; }
            if (skip) continue;
            if (section is "Idle" or "Throttle")
            {
                bool descending = section == "Idle";
                if (line.StartsWith("Length=", StringComparison.Ordinal)) line = "Length=" + Frames;
                else if (line.StartsWith("RevsFromTime=", StringComparison.Ordinal)) line = "RevsFromTime=" + string.Join(',', Enumerable.Range(0, 1024).Select(i => (Minimum + (1 - Minimum) * (descending ? 1 - i / 1023.0 : i / 1023.0)).ToString("F4", CultureInfo.InvariantCulture)));
                else if (line.StartsWith("TimeFromRevs=", StringComparison.Ordinal)) line = "TimeFromRevs=" + string.Join(',', Enumerable.Range(0, 1024).Select(i => { double value = Math.Clamp((i / 1023.0 - Minimum) / (1 - Minimum), 0, 1); return (descending ? 1 - value : value).ToString("F4", CultureInfo.InvariantCulture); }));
            }
            if ((line.StartsWith("TimeFromRevs=", StringComparison.Ordinal) || line.StartsWith("RevsFromTime=", StringComparison.Ordinal)) && line.Length > 7180) throw new IOException("Engine lookup exceeds native line dimensions.");
            lines.Add(line);
        }
        lines.AddRange(["", "[Loop]", "Throttle=1", "RevProp=1.0", "SampleRate=32000", "", "[Loop]", "Throttle=0", "RevProp=1.0", "SampleRate=32000", "", "[End]"]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllLines(output, lines, Encoding.ASCII);
    }
}
