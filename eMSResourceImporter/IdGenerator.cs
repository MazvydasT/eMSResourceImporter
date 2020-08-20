using System.Threading;

namespace eMSResourceImporter
{
    public static class IdGenerator
    {
        static int id = 0;
        public static int NextId => Interlocked.Increment(ref id);
    }
}