using System;

namespace FTPClient
{
    public class FtpException : Exception
    {
        public FtpException(string message)
            : base(message)
        {
        }
        public FtpException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

}
