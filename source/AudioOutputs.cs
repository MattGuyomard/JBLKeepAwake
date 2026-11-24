using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed class AudioOutput
{
    public readonly string Id;
    public readonly string Name;

    public AudioOutput(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() { return Name; }
}

internal sealed class AudioOutputs : IDisposable
{
    private const int Render = 0;
    private const int ConsoleRole = 0;
    private const uint Active = 1;
    private const int NotFound = unchecked((int)0x80070490);
    private const int InvalidArgument = unchecked((int)0x80070057);
    private static readonly Guid DeviceProperties = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private const uint FriendlyNameProperty = 14;
    private const ushort WideString = 31;

    private readonly IDeviceEnumerator enumerator;
    private readonly Notifications notifications;

    public AudioOutputs(Action<string, bool> changed)
    {
        var classId = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        enumerator = (IDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(classId));
        notifications = new Notifications(changed);
        try
        {
            enumerator.RegisterEndpointNotificationCallback(notifications);
        }
        catch
        {
            Marshal.ReleaseComObject(enumerator);
            throw;
        }
    }

    public string DefaultId()
    {
        IDevice device;
        int result = enumerator.GetDefaultAudioEndpoint(Render, ConsoleRole, out device);
        if (result == NotFound)
            return null;
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return device.GetId();
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public bool IsActive(string id)
    {
        IDevice device;
        int result = enumerator.GetDevice(id, out device);
        if (result == NotFound || result == InvalidArgument)
            return false;
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return device.GetState() == Active;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public List<AudioOutput> ListActive()
    {
        var outputs = new List<AudioOutput>();
        IDeviceCollection collection;
        enumerator.EnumAudioEndpoints(Render, Active, out collection);
        try
        {
            uint count = collection.GetCount();
            for (uint index = 0; index < count; index++)
            {
                IDevice device;
                collection.Item(index, out device);
                try
                {
                    outputs.Add(new AudioOutput(device.GetId(), FriendlyName(device)));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
        outputs.Sort((first, second) => StringComparer.CurrentCultureIgnoreCase.Compare(first.Name, second.Name));
        return outputs;
    }

    private static string FriendlyName(IDevice device)
    {
        IPropertyStore properties;
        device.OpenPropertyStore(0, out properties);
        try
        {
            var key = new PropertyKey { FormatId = DeviceProperties, PropertyId = FriendlyNameProperty };
            PropVariant value;
            properties.GetValue(ref key, out value);
            try
            {
                if (value.Type == WideString)
                    return Marshal.PtrToStringUni(value.Pointer) ?? "Audio output";
                return "Audio output";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(properties);
        }
    }

    public void Dispose()
    {
        try
        {
            enumerator.UnregisterEndpointNotificationCallback(notifications);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Notifications : INotifications
    {
        private readonly Action<string, bool> changed;

        public Notifications(Action<string, bool> changed)
        {
            this.changed = changed;
        }

        public int OnDeviceStateChanged(string id, uint state) { changed(id, state != Active); return 0; }
        public int OnDeviceAdded(string id) { changed(id, false); return 0; }
        public int OnDeviceRemoved(string id) { changed(id, true); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string id)
        {
            if (flow == Render && role == ConsoleRole)
                changed(null, false);
            return 0;
        }

        public int OnPropertyValueChanged(string id, PropertyKey key)
        {
            if (key.FormatId == DeviceProperties && key.PropertyId == FriendlyNameProperty)
                changed(id, false);
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer, UnionPadding;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint stateMask, out IDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);

        void RegisterEndpointNotificationCallback(INotifications callback);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(INotifications callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid id, uint context, IntPtr parameters, out IntPtr instance);
        void OpenPropertyStore(uint access, out IPropertyStore properties);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetId();

        uint GetState();
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        uint GetCount();
        void Item(uint index, out IDevice device);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        uint GetCount();
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotifications
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig]
        int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
}
