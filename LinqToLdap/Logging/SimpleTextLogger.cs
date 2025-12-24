using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LinqToLdap.Logging
{
    /// <summary>
    /// A simple implementation of <see cref="ILinqToLdapLogger"/> that writes to a <see cref="TextWriter"/>.
    /// </summary>
    public class SimpleTextLogger : ILinqToLdapLogger
    {
        private readonly WeakReference<TextWriter> _textWriter;

        /// <summary>
        /// Creates a new logger from a <see cref="TextWriter"/>.
        /// </summary>
        /// <param name="textWriter">The log destination</param>
        public SimpleTextLogger(TextWriter textWriter)
        {
            _textWriter = new WeakReference<TextWriter>(textWriter);
            TraceEnabled = true;
        }

        /// <summary>
        /// Creates a new logger with a <see cref="TraceTextWriter"/>.
        /// </summary>
        public SimpleTextLogger()
        {
            _textWriter = new WeakReference<TextWriter>(TraceTextWriter.Instance);
            TraceEnabled = true;
        }

        /// <summary>
        /// Indicates if trace logging is enabled.
        /// </summary>
        public bool TraceEnabled { get; set; }

        /// <summary>
        /// Writes the message to the trace if <see cref="TraceEnabled"/> is true.
        /// </summary>
        /// <param name="message"></param>
        public void Trace(string message)
        {
            try
            {
                if (!_textWriter.TryGetTarget(out TextWriter target)) return;
                target.WriteLine(message);
            }
            catch (Exception ex)
            {
                throw new Exception("Unable to log trace message: " + message, ex);
            }
        }

        /// <summary>
        /// Writes the optional message and exception.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="ex">The exception.</param>
        public void Error(Exception ex, string message = null)
        {
            try
            {
                if (!_textWriter.TryGetTarget(out TextWriter target)) return;

                if (message != null) target.WriteLine(message);
                ObjectDumper.Write(ex, 0, target);
            }
            catch
            {
                throw new Exception("Unable to log exception", ex);
            }
        }
    }
}