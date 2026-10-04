using System.Runtime.InteropServices;

namespace SoundKeeper.GUI.Services;

public readonly record struct AudioOutput(string Id, string Name);

// Active Windows audio outputs, enumerated like the engine does (MMDevice API): endpoint ID + Windows friendly name.
public sealed class AudioOutputService
{
    private const int RenderFlow = 0;
    private const uint ActiveState = 0x1;
    private const uint ReadAccess = 0;
    private const ushort WideStringType = 31; // VT_LPWSTR
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public IReadOnlyList<AudioOutput> GetActiveOutputs()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            enumerator.EnumAudioEndpoints(RenderFlow, ActiveState, out var collection);
            try
            {
                collection.GetCount(out var count);
                var outputs = new List<AudioOutput>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    collection.Item(index, out var device);
                    try
                    {
                        device.GetId(out var id);
                        outputs.Add(new AudioOutput(id, GetFriendlyName(device)));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
                return outputs;
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(ReadAccess, out var store);
        try
        {
            var key = FriendlyNameKey;
            store.GetValue(ref key, out var value);
            try
            {
                return value.ValueType == WideStringType ? Marshal.PtrToStringUni(value.Pointer) ?? string.Empty : string.Empty;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(uint access, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort ValueType;
    [FieldOffset(8)] public IntPtr Pointer;
}
