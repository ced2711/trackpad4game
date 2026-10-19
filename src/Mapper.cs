using System;
using System.Windows.Forms;

namespace Trackpad4Game
{
    // Decides what happens to each key and mouse-button event. It has no Win32 calls,
    // so the rules can be unit tested; Engine feeds it events from the hooks.
    class Mapper
    {
        public Keys LeftKey = Keys.V, RightKey = Keys.B, ToggleKey = Keys.Oemtilde;
        public Action<bool, bool> Send = delegate { }; // (left button?, down?)
        public Action Toggle = delegate { };

        public bool LeftHeld { get; private set; }  // mouse buttons we pressed on behalf of a key
        public bool RightHeld { get; private set; }
        bool toggleHeld;                            // the toggle key's down was swallowed, so swallow its up too
        bool blockedLeft, blockedRight;             // touchpad presses we swallowed; swallow their releases too

        // active: switched on and allowed to act here. toggleAllowed: the toggle key may be used here.
        // Returns true when the key event must be swallowed.
        public bool OnKey(Keys key, bool down, bool active, bool toggleAllowed)
        {
            // Always finish a click we started, even if the mode or the focused window changed meanwhile.
            if (!down)
            {
                if (key == LeftKey && LeftHeld) { LeftHeld = false; Send(true, false); return true; }
                if (key == RightKey && RightHeld) { RightHeld = false; Send(false, false); return true; }
                if (key == ToggleKey && toggleHeld) { toggleHeld = false; return true; }
            }

            if (key == ToggleKey && toggleAllowed)
            {
                if (down && !toggleHeld) { toggleHeld = true; Toggle(); }
                return down; // an up without our down belongs to someone else
            }

            if (active)
            {
                if (key == LeftKey)
                {
                    if (down && !LeftHeld) { LeftHeld = true; Send(true, true); } // key auto-repeat is ignored
                    return true;
                }
                if (key == RightKey)
                {
                    if (down && !RightHeld) { RightHeld = true; Send(false, true); }
                    return true;
                }
            }
            return false;
        }

        // A real (not ours) left/right button event. block: blocking applies at this moment and place.
        // Returns true when it must be swallowed.
        public bool OnButton(bool left, bool down, bool block)
        {
            if (down)
            {
                if (!block) return false;
                if (left) blockedLeft = true; else blockedRight = true;
                return true;
            }
            // Swallow only releases whose press we swallowed; any other release must pass,
            // or the button would stay stuck down.
            bool pressBlocked = left ? blockedLeft : blockedRight;
            if (left) blockedLeft = false; else blockedRight = false;
            return pressBlocked;
        }

        public void ReleaseHeld()
        {
            if (LeftHeld) { LeftHeld = false; Send(true, false); }
            if (RightHeld) { RightHeld = false; Send(false, false); }
        }
    }
}
