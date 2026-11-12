namespace BoostRoot.Core.Hardware;

/// <summary>Parses SMBIOS "Memory Device" (type 17) entries from a raw RSMB firmware table.</summary>
public static class SmbiosMemory
{
    /// <param name="rsmb">Bytes returned by GetSystemFirmwareTable('RSMB', 0): 8-byte header then the structure table.</param>
    public static IReadOnlyList<RamModule> Parse(ReadOnlySpan<byte> rsmb)
    {
        var list = new List<RamModule>();
        if (rsmb.Length < 8) return list;
        var length = BitConverter.ToInt32(rsmb[4..8]);
        var table = rsmb.Slice(8, Math.Min(length, rsmb.Length - 8));
        var i = 0;
        while (i + 4 <= table.Length)
        {
            var type = table[i];
            var len = table[i + 1];
            if (len < 4 || i + len > table.Length) break;
            if (type == 17 && len >= 0x22)
            {
                var s = table.Slice(i, len);
                var sizeRaw = BitConverter.ToUInt16(s[0x0C..0x0E]);
                var sizeMb = sizeRaw == 0x7FFF && len >= 0x20 ? BitConverter.ToInt32(s[0x1C..0x20]) : (sizeRaw & 0x8000) != 0 ? (sizeRaw & 0x7FFF) / 1024 : sizeRaw;
                int speed = BitConverter.ToUInt16(s[0x15..0x17]);
                int configured = BitConverter.ToUInt16(s[0x20..0x22]);
                if (speed == 0xFFFF && len >= 0x58) speed = BitConverter.ToInt32(s[0x54..0x58]);
                if (configured == 0xFFFF && len >= 0x5C) configured = BitConverter.ToInt32(s[0x58..0x5C]);
                if (sizeMb > 0 && sizeRaw != 0xFFFF) list.Add(new RamModule(sizeMb, speed, configured));
            }
            if (type == 127) break;
            // skip formatted area, then the string set terminated by a double NUL
            var j = i + len;
            while (j + 1 < table.Length && !(table[j] == 0 && table[j + 1] == 0)) j++;
            i = j + 2;
        }
        return list;
    }
}
