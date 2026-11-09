using System.Buffers.Binary;

namespace GlassDock.Windows.Desktop;

// Format adapted from kvnxiao/win-nightlight-cli (MIT), commit 0461529455f3594b9b9d6cc3d6c2e7a9736de9b5.
// Unknown fields retain their original headers and encoded values at every envelope level.
internal sealed class NightLightCloudStore
{
    private const int MaxBytes = 65536;
    private sealed record Field(ushort Id, byte Type, byte[] Header, byte[] Value);
    private readonly List<Field> outer, container, wrapper, state;
    internal bool On { get; }
    internal bool Usable { get; }
    internal ulong Modified { get; }
    internal ulong LastTransition { get; }

    internal NightLightCloudStore(byte[] data)
    {
        outer = Document(data);
        container = Structure(Required(outer, 1, 10).Value);
        Modified = Number(Required(container, 0, 6));
        if (Modified > 253402300799) throw Invalid();
        wrapper = Structure(Required(container, 1, 10).Value);
        var blob = Required(wrapper, 1, 11).Value;
        var reader = new Reader(blob);
        if (reader.Byte() != 14) throw Invalid(); // list<int8>, containing a marshaled CB v1 document
        var length = reader.Count();
        state = Document(reader.Bytes(length)); reader.End();
        var status = Optional(state, 0, 16);
        if (status is not null && Number(status) != 0) throw Invalid();
        On = status is not null;
        var cause = Optional(state, 10, 16);
        if (cause is not null && Number(cause) is not (0 or 2)) throw Invalid(); // ZigZag scheduled=0/manual=1
        // Only initialized state records are writable; do not invent a state from an empty/default payload.
        LastTransition = Number(Required(state, 20, 6));
        if (LastTransition == 0 || LastTransition > 2650467743999999999) throw Invalid();
        var usable = Optional(state, 30, 2);
        Usable = usable is null || usable.Value[0] == 1;
    }

    internal byte[] SetActive(bool on, DateTimeOffset now, ulong highestWritten)
    {
        if (!Usable) throw new NotSupportedException("Windows reports Night Light unavailable on this display.");
        if (on == On) throw new InvalidOperationException("Night Light is already in the requested state.");
        var modified = Math.Max((ulong)now.ToUnixTimeSeconds(), Math.Max(checked(Modified + 2), checked(highestWritten + 2)));
        if (modified > 253402300799) throw Invalid();
        var fields = Replace(state, 0, on ? Numeric(0, 16, 0) : null);
        fields = Replace(fields, 10, Numeric(10, 16, 2));
        fields = Replace(fields, 20, Numeric(20, 6, checked((ulong)now.UtcDateTime.ToFileTimeUtc())));
        var payload = Encode(fields, marshaled: true);
        var blob = new List<byte> { 14 }; Varint(blob, (ulong)payload.Length); blob.AddRange(payload);
        var nextWrapper = Replace(wrapper, 1, WithValue(Required(wrapper, 1, 11), blob.ToArray()));
        var nextContainer = Replace(container, 0, WithValue(Required(container, 0, 6), EncodedNumber(modified)));
        nextContainer = Replace(nextContainer, 1, WithValue(Required(container, 1, 10), Encode(nextWrapper)));
        var result = Encode(Replace(outer, 1, WithValue(Required(outer, 1, 10), Encode(nextContainer))), marshaled: true);
        _ = new NightLightCloudStore(result); // Validate the rebuilt document before any registry write.
        return result;
    }

    private static Field WithValue(Field field, byte[] value) => field with { Value = value };
    private static List<Field> Replace(List<Field> fields, ushort id, Field? replacement)
    {
        var result = fields.Where(field => field.Id != id).ToList();
        if (replacement is not null)
        {
            var index = result.FindIndex(field => field.Id > id);
            result.Insert(index < 0 ? result.Count : index, replacement);
        }
        return result;
    }

    private static Field Numeric(ushort id, byte type, ulong value)
    {
        var header = new List<byte>();
        if (id < 6) header.Add((byte)((id << 5) | type));
        else if (id <= byte.MaxValue) { header.Add((byte)(0xC0 | type)); header.Add((byte)id); }
        else { header.Add((byte)(0xE0 | type)); header.Add((byte)id); header.Add((byte)(id >> 8)); }
        return new(id, type, header.ToArray(), EncodedNumber(value));
    }

    private static byte[] EncodedNumber(ulong value) { var bytes = new List<byte>(); Varint(bytes, value); return bytes.ToArray(); }
    private static void Varint(List<byte> bytes, ulong value)
    {
        while (value >= 128) { bytes.Add((byte)((value & 127) | 128)); value >>= 7; }
        bytes.Add((byte)value);
    }

    private static byte[] Encode(List<Field> fields, bool marshaled = false)
    {
        var bytes = new List<byte>();
        if (marshaled) { bytes.AddRange("CB"u8.ToArray()); bytes.Add(1); bytes.Add(0); }
        foreach (var field in fields) { bytes.AddRange(field.Header); bytes.AddRange(field.Value); }
        bytes.Add(0);
        if (bytes.Count > MaxBytes) throw Invalid();
        return bytes.ToArray();
    }

    private static Field Required(List<Field> fields, ushort id, byte type) => Optional(fields, id, type) ?? throw Invalid();
    private static Field? Optional(List<Field> fields, ushort id, byte type)
    {
        var field = fields.SingleOrDefault(field => field.Id == id);
        if (field is not null && field.Type != type) throw Invalid();
        return field;
    }
    private static ulong Number(Field field) { var reader = new Reader(field.Value); var value = reader.Unsigned(); reader.End(); return value; }
    private static List<Field> Document(byte[] bytes)
    {
        var reader = new Reader(bytes);
        if (reader.Byte() != 'C' || reader.Byte() != 'B' || reader.Byte() != 1 || reader.Byte() != 0) throw Invalid();
        var fields = reader.Fields(0); reader.End(); return fields;
    }
    private static List<Field> Structure(byte[] bytes) { var reader = new Reader(bytes); var fields = reader.Fields(0); reader.End(); return fields; }
    private static InvalidDataException Invalid() => new("Unsupported or malformed Night Light CloudStore format; no state was written.");

    private sealed class Reader(byte[] data)
    {
        private int position;
        internal byte Byte() => position < data.Length ? data[position++] : throw Invalid();
        internal byte[] Bytes(int count)
        {
            if (count < 0 || count > data.Length - position) throw Invalid();
            var bytes = data.AsSpan(position, count).ToArray(); position += count; return bytes;
        }
        internal void End() { if (position != data.Length) throw Invalid(); }
        internal ulong Unsigned()
        {
            ulong value = 0;
            for (var shift = 0; shift <= 63; shift += 7)
            {
                var next = Byte();
                if (shift == 63 && next > 1) throw Invalid();
                value |= (ulong)(next & 127) << shift;
                if (next < 128) return value;
            }
            throw Invalid();
        }
        internal int Count()
        {
            var count = Unsigned();
            if (count > MaxBytes || count > (ulong)(data.Length - position)) throw Invalid();
            return (int)count;
        }
        internal List<Field> Fields(int depth)
        {
            if (data.Length > MaxBytes || depth > 16) throw Invalid();
            var fields = new List<Field>(); var ids = new HashSet<ushort>();
            while (true)
            {
                var start = position; var header = Byte();
                if (header == 0) return fields;
                var type = (byte)(header & 31); var bits = header >> 5;
                ushort id = bits switch { 6 => Byte(), 7 => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2)), _ => (ushort)bits };
                if (!ids.Add(id)) throw Invalid();
                var valueStart = position;
                Skip(type, depth);
                fields.Add(new(id, type, data[start..valueStart], data[valueStart..position]));
            }
        }
        private void Skip(byte type, int depth)
        {
            if (depth > 16) throw Invalid();
            switch (type)
            {
                case 2: if (Byte() > 1) throw Invalid(); break;
                case 3 or 14: Byte(); break;
                case 4 or 15: if (Unsigned() > ushort.MaxValue) throw Invalid(); break;
                case 5 or 16: if (Unsigned() > uint.MaxValue) throw Invalid(); break;
                case 6 or 17: Unsigned(); break;
                case 7: Bytes(4); break;
                case 8: Bytes(8); break;
                case 9: Bytes(Count()); break;
                case 18: Bytes(checked(Count() * 2)); break;
                case 10: Fields(depth + 1); break;
                case 11 or 12:
                    var itemType = Byte(); var count = Count();
                    for (var i = 0; i < count; i++) Skip(itemType, depth + 1);
                    break;
                case 13:
                    var keyType = Byte(); var valueType = Byte(); var pairs = Count();
                    for (var i = 0; i < pairs; i++) { Skip(keyType, depth + 1); Skip(valueType, depth + 1); }
                    break;
                default: throw Invalid(); // Includes STOP_BASE and future types: never flatten an unknown schema.
            }
        }
    }
}
