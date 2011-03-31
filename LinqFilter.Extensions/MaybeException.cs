using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WellDunne.Extensions
{
    public sealed class MaybeException<T>
    {
        public T Value { get; private set; }
        public Exception Exception { get; private set; }
        public bool IsSuccessful { get; private set; }

        public MaybeException(T value)
        {
            this.Value = value;
            this.Exception = null;
            this.IsSuccessful = true;
        }

        public MaybeException(Exception exception)
        {
            this.Value = default(T);
            this.Exception = exception;
            this.IsSuccessful = false;
        }

        public static implicit operator T(MaybeException<T> maybe) 
        {
            if (!maybe.IsSuccessful) throw new InvalidOperationException("MaybeException is in a faulted state! Always ensure that (IsSuccessful == true) before using the implicit conversion operator.");
            return maybe.Value;
        }

        public static implicit operator MaybeException<T>(T value)
        {
            return new MaybeException<T>(value);
        }

        public static explicit operator MaybeException<T>(Exception ex)
        {
            return new MaybeException<T>(ex);
        }
    }
}
