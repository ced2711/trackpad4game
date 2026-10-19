using System.Runtime.InteropServices;

namespace Trackpad4Game
{
    // Precision touchpad sensitivity, which is Windows' palm rejection: at anything but
    // "Most sensitive", the pointer freezes for a moment after each key press, so aiming
    // while holding a click key or W/A/S/D does not work. Needs Windows 11 and a precision touchpad.
    static class Touchpad
    {
        const uint SPI_GETTOUCHPADPARAMETERS = 0xAE, SPI_SETTOUCHPADPARAMETERS = 0xAF;
        public const int MostSensitive = 0;

        static bool Get(out Native.TOUCHPAD_PARAMETERS p)
        {
            p = new Native.TOUCHPAD_PARAMETERS { versionNumber = 1 };
            try
            {
                return Native.SystemParametersInfo(SPI_GETTOUCHPADPARAMETERS, (uint)Marshal.SizeOf(p), ref p, 0)
                    && (p.flags1 & 1) != 0; // touchpadPresent
            }
            catch { return false; }
        }

        public static bool Supported { get { Native.TOUCHPAD_PARAMETERS p; return Get(out p); } }

        // 0 (most sensitive) to 4 (least sensitive), or -1 when there is no precision touchpad.
        public static int Sensitivity
        {
            get { Native.TOUCHPAD_PARAMETERS p; return Get(out p) ? p.sensitivityLevel : -1; }
        }

        public static bool SetSensitivity(int level)
        {
            Native.TOUCHPAD_PARAMETERS p;
            if (!Get(out p)) return false;
            if (p.sensitivityLevel == level) return true;
            p.versionNumber = 1;
            p.sensitivityLevel = level;
            try { return Native.SystemParametersInfo(SPI_SETTOUCHPADPARAMETERS, (uint)Marshal.SizeOf(p), ref p, 2); } // SPIF_SENDCHANGE
            catch { return false; }
        }
    }
}
