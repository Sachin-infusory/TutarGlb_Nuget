using System;

namespace tutar_glb.Utils
{
    public static class Exceptions
    {
        [Serializable]
        public class NetworkException : Exception
        {
            public NetworkException() { }

            public NetworkException(string error) : base(error) { }
        }
    }



    public class TutarGlbInitializationException : Exception
    {
        public TutarGlbInitializationException(string message) : base(message) { }
        public TutarGlbInitializationException(string message, Exception innerException) : base(message, innerException) { }
    }


    /// <summary>
    /// Exception thrown when API verification fails
    /// </summary>
    public class TutarGlbVerificationException : Exception
    {
        public int StatusCode { get; }
        public TutarGlbVerificationException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }
    }

}
