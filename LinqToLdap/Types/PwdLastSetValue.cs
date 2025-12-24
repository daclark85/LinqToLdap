using System;

namespace LinqToLdap.Types
{
    /// <summary>
    /// Represents the pwdLastSet Active Directory attribute with special handling for write operations.
    /// </summary>
    /// <remarks>
    /// Reading: Returns the file time value from AD as a DateTime.
    /// Writing: 
    ///   - 0: Forces a password change at next logon
    ///   - -1: Sets the timestamp to "Now" (resets the expiration clock)
    ///   - DateTime value: Converts to file time for write operations
    /// </remarks>
    public readonly struct PwdLastSetValue : IEquatable<PwdLastSetValue>
    {
        private readonly long _fileTime;

        /// <summary>
        /// Gets the DateTime representation of the pwdLastSet value.
        /// Returns null if the value is 0 (never set or must change password).
        /// </summary>
        public DateTime? DateTime => _fileTime == 0 ? null : System.DateTime.FromFileTime(_fileTime);

        /// <summary>
        /// Gets the raw file time value.
        /// </summary>
        public long FileTime => _fileTime;

        /// <summary>
        /// Gets a value indicating whether the user must change their password at next logon.
        /// </summary>
        public bool MustChangePassword => _fileTime == 0;

        private PwdLastSetValue(long fileTime)
        {
            _fileTime = fileTime;
        }

        /// <summary>
        /// Creates a PwdLastSet from a file time value (typically when reading from AD).
        /// </summary>
        /// <param name="fileTime">The file time value from Active Directory.</param>
        /// <returns>A new <see cref="PwdLastSetValue"/> instance.</returns>
        public static PwdLastSetValue FromFileTime(long fileTime) => new(fileTime);

        /// <summary>
        /// Creates a PwdLastSet from a DateTime value.
        /// </summary>
        /// <param name="dateTime">The DateTime to convert to file time.</param>
        /// <returns>A new <see cref="PwdLastSetValue"/> instance.</returns>
        public static PwdLastSetValue FromDateTime(DateTime dateTime) => new(dateTime.ToFileTime());

        /// <summary>
        /// Creates a PwdLastSet that forces a password change at next logon.
        /// </summary>
        /// <returns>A new <see cref="PwdLastSetValue"/> instance with value 0.</returns>
        public static PwdLastSetValue ForcePasswordChange() => new(0);

        /// <summary>
        /// Creates a PwdLastSet that sets the timestamp to "Now" when written to AD.
        /// </summary>
        /// <returns>A new <see cref="PwdLastSetValue"/> instance with value -1.</returns>
        public static PwdLastSetValue SetToNow() => new(-1);

        /// <summary>
        /// Gets the string representation for writing to AD.
        /// </summary>
        /// <returns>The file time value as a string.</returns>
        public string ToDirectoryValue() => _fileTime.ToString();

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>true if the current object is equal to the other parameter; otherwise, false.</returns>
        public bool Equals(PwdLastSetValue other) => _fileTime == other._fileTime;

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object obj) => obj is PwdLastSetValue other && Equals(other);

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode() => _fileTime.GetHashCode();

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string representation of the pwdLastSet value.</returns>
        public override string ToString() => 
            _fileTime == 0 ? "Must change password" :
            _fileTime == -1 ? "Set to now" :
            DateTime?.ToString() ?? string.Empty;

        /// <summary>
        /// Determines whether two specified instances of <see cref="PwdLastSetValue"/> are equal.
        /// </summary>
        /// <param name="left">The first instance to compare.</param>
        /// <param name="right">The second instance to compare.</param>
        /// <returns>true if left and right are equal; otherwise, false.</returns>
        public static bool operator ==(PwdLastSetValue left, PwdLastSetValue right) => left.Equals(right);

        /// <summary>
        /// Determines whether two specified instances of <see cref="PwdLastSetValue"/> are not equal.
        /// </summary>
        /// <param name="left">The first instance to compare.</param>
        /// <param name="right">The second instance to compare.</param>
        /// <returns>true if left and right are not equal; otherwise, false.</returns>
        public static bool operator !=(PwdLastSetValue left, PwdLastSetValue right) => !left.Equals(right);
    }
}