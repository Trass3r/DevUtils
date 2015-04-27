// Guids.cs
// MUST match guids.h
using System;

namespace VSPackage.DevUtils
{
    internal static class GuidList
    {
        public const string guidDevUtilsPkgString = "19b8a882-47f2-4fdd-a657-5f15a2c5ecae";
        public const string guidDevUtilsCmdSetString = "57945603-4aa5-4f1b-85c4-b3c450332e5e";

        public static readonly Guid guidDevUtilsCmdSet = new Guid(guidDevUtilsCmdSetString);
    };
}