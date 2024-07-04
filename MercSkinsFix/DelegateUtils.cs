using System;

namespace MercSkinsFix
{
    static class DelegateUtils
    {
        public static void Append<TDelegate>(ref TDelegate original, TDelegate append) where TDelegate : Delegate
        {
            original = (TDelegate)Delegate.Combine(original, append);
        }
    }
}
