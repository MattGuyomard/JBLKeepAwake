using System;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class KeepAwakeSignal : IDisposable
{
    private const uint InvalidHandle = 5;
    private const uint QueryInstanceId = 0x0800 + 17;
    private const uint QueryInstanceIdSize = 0x0800 + 18;
    private const uint BeginLoop = 0x04;
    private const uint EndLoop = 0x08;
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(WaveHeader));
    private static readonly int FlagsOffset = Marshal.OffsetOf(typeof(WaveHeader), "Flags").ToInt32();

    private IntPtr device;
    private IntPtr buffer;
    private IntPtr header;
    private bool prepared;
    private bool playing;

    public void Start(string outputId, int frequency, int level)
    {
        Stop();

        uint output = FindOutput(outputId);

        // The smallest whole number of periods that fits an integer number
        // of PCM frames lets Windows repeat the buffer without a seam.
        int frames = SampleRate / GreatestCommonDivisor(SampleRate, frequency);
        var samples = new short[frames * Channels];
        double amplitude = short.MaxValue * Math.Pow(10, level / 20.0);
        for (int i = 0; i < samples.Length / Channels; i++)
        {
            short sample = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
            samples[2 * i] = sample;
            samples[2 * i + 1] = sample;
        }

        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = Channels,
            SamplesPerSecond = SampleRate,
            BytesPerSecond = SampleRate * Channels * sizeof(short),
            BlockAlign = Channels * sizeof(short),
            BitsPerSample = 16
        };

        try
        {
            IntPtr openedDevice;
            Check(waveOutOpen(out openedDevice, output, ref format, IntPtr.Zero, IntPtr.Zero, 0),
                "Could not open the audio output");
            device = openedDevice;

            int length = samples.Length * sizeof(short);
            buffer = Marshal.AllocHGlobal(length);
            Marshal.Copy(samples, 0, buffer, samples.Length);
            header = Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.StructureToPtr(new WaveHeader
            {
                Data = buffer,
                BufferLength = (uint)length,
                Loops = uint.MaxValue
            }, header, false);

            Check(waveOutPrepareHeader(device, header, HeaderSize), "Could not prepare the signal");
            prepared = true;
            int flags = Marshal.ReadInt32(header, FlagsOffset);
            Marshal.WriteInt32(header, FlagsOffset, flags | (int)(BeginLoop | EndLoop));
            Check(waveOutWrite(device, header, HeaderSize), "Could not play the signal");
            playing = true;
        }
        catch
        {
            Release();
            throw;
        }
    }

    public bool IsPlaying { get { return playing; } }

    public void Stop()
    {
        Check(Release(), "Could not stop the signal");
    }

    public void Dispose()
    {
        Release();
    }

    private uint Release()
    {
        if (device != IntPtr.Zero)
        {
            uint result = playing ? waveOutReset(device) : 0;
            if (result == 0)
            {
                playing = false;
                if (prepared)
                    result = waveOutUnprepareHeader(device, header, HeaderSize);
            }
            if (result == 0)
            {
                prepared = false;
                result = waveOutClose(device);
            }
            if (result != 0 && result != InvalidHandle)
                return result;

            device = IntPtr.Zero;
            playing = false;
            prepared = false;
        }

        // Keep the allocations until the driver has returned ownership.
        Marshal.FreeHGlobal(header);
        Marshal.FreeHGlobal(buffer);
        header = IntPtr.Zero;
        buffer = IntPtr.Zero;
        return 0;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            int remainder = left % right;
            left = right;
            right = remainder;
        }
        return left;
    }

    private static uint FindOutput(string id)
    {
        // Resolve the endpoint each time because waveOut numbers can change
        // when a speaker disconnects or reconnects.
        int bytes = (id.Length + 1) * sizeof(char);
        IntPtr buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            uint count = waveOutGetNumDevs();
            for (uint output = 0; output < count; output++)
            {
                var handle = new IntPtr(output);
                UIntPtr size;
                if (waveOutMessage(handle, QueryInstanceIdSize, out size, UIntPtr.Zero) != 0
                    || size.ToUInt64() != (ulong)bytes)
                    continue;

                if (waveOutMessage(handle, QueryInstanceId, buffer, size) == 0
                    && string.Equals(id, Marshal.PtrToStringUni(buffer, id.Length), StringComparison.OrdinalIgnoreCase))
                    return output;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        throw new InvalidOperationException("The audio output is not available.");
    }

    private static void Check(uint result, string action)
    {
        if (result == 0)
            return;

        var message = new StringBuilder(256);
        string detail = waveOutGetErrorText(result, message, (uint)message.Capacity) == 0
            ? message.ToString()
            : "Windows audio error " + result + ".";
        throw new InvalidOperationException(action + ".\n\n" + detail);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSecond, BytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint waveOutMessage(IntPtr device, uint message, out UIntPtr parameter, UIntPtr length);

    [DllImport("winmm.dll")]
    private static extern uint waveOutMessage(IntPtr device, uint message, IntPtr parameter, UIntPtr length);

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormat format,
        IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutReset(IntPtr device);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(IntPtr device);

    [DllImport("winmm.dll", EntryPoint = "waveOutGetErrorTextW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint waveOutGetErrorText(uint result, StringBuilder text, uint length);
}
