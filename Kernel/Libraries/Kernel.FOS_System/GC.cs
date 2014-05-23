﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kernel.FOS_System
{
    /// <summary>
    /// The garbage collector.
    /// </summary>
    /// <remarks>
    /// Make sure all methods that the GC calls are marked with [Compiler.NoGC] (including
    /// get-set property methods! Apply the attribute to the get/set keywords not the property
    /// declaration (/name).
    /// </remarks>
    [Compiler.PluggedClass]
    public static unsafe class GC
    {
        /// <summary>
        /// The total number of objects currently allocated by the GC.
        /// </summary>
        public static int NumObjs = 0;
        /// <summary>
        /// Whether the GC has been initialised yet or not.
        /// Used to prevent the GC running before it has been initialised properly.
        /// </summary>
        private static bool GCInitialised = false;
        /// <summary>
        /// Whether the GC is currently executing. Used to prevent the GC calling itself (or ending up in loops with
        /// called methods re-calling the GC!)
        /// </summary>
        public static bool InsideGC = false;

        public static int numStrings = 0;

        private static ObjectToCleanup* CleanupList;

        //TODO - GC needs an object reference tree to do a thorough scan to find reference loops

        /// <summary>
        /// Intialises the GC.
        /// </summary>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void Init()
        {
            Heap.InitFixedHeap();
            GCInitialised = true;
        }

        /// <summary>
        /// Creates a new object of specified type (but does not call the default constructor).
        /// </summary>
        /// <param name="theType">The type of object to create.</param>
        /// <returns>A pointer to the new object in memory.</returns>
        [Compiler.NewObjMethod]
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void* NewObj(FOS_System.Type theType)
        {
            if(!GCInitialised || InsideGC)
            {
                return null;
            }

            InsideGC = true;

            //Alloc space for GC header that prefixes object data
            //Alloc space for new object
            
            uint totalSize = theType.Size;
            totalSize += (uint)sizeof(GCHeader);

            GCHeader* newObjPtr = (GCHeader*)Heap.Alloc(totalSize);
            
            if((UInt32)newObjPtr == 0)
            {
                InsideGC = false;

                return null;
            }

            NumObjs++;

            //Initialise the GCHeader
            SetSignature(newObjPtr);
            newObjPtr->RefCount = 1;
            //Initialise the object _Type field
            FOS_System.ObjectWithType newObj = (FOS_System.ObjectWithType)Utilities.ObjectUtilities.GetObject(newObjPtr + 1);
            newObj._Type = theType;
            
            byte* newObjBytePtr = (byte*)newObjPtr;
            for (int i = sizeof(GCHeader) + 4/*For _Type field*/; i < totalSize; i++)
            {
                newObjBytePtr[i] = 0;
            }

            //Move past GCHeader
            newObjBytePtr = (byte*)(newObjBytePtr + sizeof(GCHeader));

            InsideGC = false;

            return newObjBytePtr;
        }

        /// <summary>
        /// Creates a new array with specified element type (but does not call the default constructor).
        /// </summary>
        /// <remarks>"length" param placed first so that calling NewArr method is simple
        /// with regards to pushing params onto the stack.</remarks>
        /// <param name="theType">The type of element in the array to create.</param>
        /// <returns>A pointer to the new array in memory.</returns>
        [Compiler.NewArrMethod]
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void* NewArr(int length, FOS_System.Type elemType)
        {
            int arrayObjSize = 8;

            if (!GCInitialised || InsideGC)
            {
                return null;
            }

            if (length < 0)
            {
                ExceptionMethods.Throw_OverflowException();
            }

            InsideGC = true;

            //Alloc space for GC header that prefixes object data
            //Alloc space for new array object
            //Alloc space for new array elems

            uint totalSize = ((FOS_System.Type)typeof(FOS_System.Array)).Size;
            totalSize += elemType.StackSize * (uint)length;
            totalSize += (uint)sizeof(GCHeader);

            GCHeader* newObjPtr = (GCHeader*)Heap.Alloc(totalSize);

            if ((UInt32)newObjPtr == 0)
            {
                InsideGC = false;

                return null;
            }

            NumObjs++;

            //Initialise the GCHeader
            SetSignature(newObjPtr);
            newObjPtr->RefCount = 1;

            FOS_System.Array newArr = (FOS_System.Array)Utilities.ObjectUtilities.GetObject(newObjPtr + 1);
            newArr._Type = (FOS_System.Type)typeof(FOS_System.Array);
            newArr.length = length;
            newArr.elemType = elemType;
            
            byte* newObjBytePtr = (byte*)newObjPtr;
            for (int i = sizeof(GCHeader) + arrayObjSize + 4/*For _Type field*/; i < totalSize; i++)
            {
                newObjBytePtr[i] = 0;
            }

            //Move past GCHeader
            newObjBytePtr = (byte*)(newObjBytePtr + sizeof(GCHeader));

            InsideGC = false;
            
            return newObjBytePtr;
        }

        /// <summary>
        /// DO NOT CALL DIRECTLY. Use FOS_System.String.New
        /// Creates a new string with specified length (but does not call the default constructor).
        /// </summary>
        /// <returns>A pointer to the new string in memory.</returns>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void* NewString(int length)
        {
            int strObjSize = 4;

            if (!GCInitialised || InsideGC)
            {
                return null;
            }

            if (length < 0)
            {
                ExceptionMethods.Throw_OverflowException();
            }

            InsideGC = true;

            //Alloc space for GC header that prefixes object data
            //Alloc space for new string object
            //Alloc space for new string chars

            uint totalSize = ((FOS_System.Type)typeof(FOS_System.String)).Size;
            totalSize += /*char size in bytes*/2 * (uint)length;
            totalSize += (uint)sizeof(GCHeader);

            GCHeader* newObjPtr = (GCHeader*)Heap.Alloc(totalSize);

            if ((UInt32)newObjPtr == 0)
            {
                InsideGC = false;

                return null;
            }

            NumObjs++;
            numStrings++;

            //Initialise the GCHeader
            SetSignature(newObjPtr);
            //RefCount to 0 initially because of FOS_System.String.New should be used
            //      - In theory, New should be called, creates new string and passes it back to caller
            //        Caller is then required to store the string in a variable resulting in inc.
            //        ref count so ref count = 1 in only stored location. 
            //        Caller is not allowed to just "discard" (i.e. use Pop IL op or C# that generates
            //        Pop IL op) so ref count will always at some point be incremented and later
            //        decremented by managed code. OR the variable will stay in a static var until
            //        the OS exits...

            newObjPtr->RefCount = 0;

            FOS_System.String newStr = (FOS_System.String)Utilities.ObjectUtilities.GetObject(newObjPtr + 1);
            newStr._Type = (FOS_System.Type)typeof(FOS_System.String);
            newStr.length = length;
            
            byte* newObjBytePtr = (byte*)newObjPtr;
            for (int i = sizeof(GCHeader) + strObjSize + 4/*For _Type field*/; i < totalSize; i++)
            {
                newObjBytePtr[i] = 0;
            }

            //Move past GCHeader
            newObjBytePtr = (byte*)(newObjBytePtr + sizeof(GCHeader));

            InsideGC = false;

            return newObjBytePtr;
        }

        /// <summary>
        /// Increments the ref count of a GC managed object.
        /// </summary>
        /// <remarks>
        /// Uses underlying increment ref count method.
        /// </remarks>
        /// <param name="anObj">The object to increment the ref count of.</param>
        [Compiler.IncrementRefCountMethod]
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void IncrementRefCount(FOS_System.Object anObj)
        {
            if (!GCInitialised || InsideGC || anObj == null)
            {
                return;
            }

            InsideGC = true;

            byte* objPtr = (byte*)Utilities.ObjectUtilities.GetHandle(anObj);
            _IncrementRefCount(objPtr);

            InsideGC = false;
        }
        /// <summary>
        /// Underlying method that increments the ref count of a GC managed object.
        /// </summary>
        /// <remarks>
        /// This method checks that the pointer is not a null pointer and also checks for the GC signature 
        /// so string literals and the like don't accidentally get treated as normal GC managed strings.
        /// </remarks>
        /// <param name="objPtr">Pointer to the object to increment the ref count of.</param>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void _IncrementRefCount(byte* objPtr)
        {
            objPtr -= sizeof(GCHeader);
            GCHeader* gcHeaderPtr = (GCHeader*)objPtr;
            if (CheckSignature(gcHeaderPtr))
            {
                gcHeaderPtr->RefCount++;

                if (gcHeaderPtr->RefCount > 0)
                {
                    RemoveObjectToCleanup(gcHeaderPtr);
                }
            }
        }

        /// <summary>
        /// Decrements the ref count of a GC managed object.
        /// </summary>
        /// <remarks>
        /// This method checks that the pointer is not a null pointer and also checks for the GC signature 
        /// so string literals and the like don't accidentally get treated as normal GC managed strings.
        /// </remarks>
        /// <param name="anObj">The object to decrement the ref count of.</param>
        [Compiler.DecrementRefCountMethod]
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void DecrementRefCount(FOS_System.Object anObj)
        {
            DecrementRefCount(anObj, false);
        }
        /// <summary>
        /// Decrements the ref count of a GC managed object.
        /// </summary>
        /// <remarks>
        /// This method checks that the pointer is not a null pointer and also checks for the GC signature 
        /// so string literals and the like don't accidentally get treated as normal GC managed strings.
        /// </remarks>
        /// <param name="anObj">The object to decrement the ref count of.</param>
        /// <param name="overrideInside">Whether to ignore the InsideGC test or not.</param>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void DecrementRefCount(FOS_System.Object anObj, bool overrideInside)
        {
            if (!GCInitialised || (InsideGC && !overrideInside) || anObj == null)
            {
                return;
            }

            if (!overrideInside)
            {
                InsideGC = true;
            }

            byte* objPtr = (byte*)Utilities.ObjectUtilities.GetHandle(anObj);
            _DecrementRefCount(objPtr);

            if (!overrideInside)
            {
                InsideGC = false;
            }
        }
        /// <summary>
        /// Underlying method that decrements the ref count of a GC managed object.
        /// </summary>
        /// <remarks>
        /// This method checks that the pointer is not a null pointer and also checks for the GC signature 
        /// so string literals and the like don't accidentally get treated as normal GC managed strings.
        /// </remarks>
        /// <param name="objPtr">A pointer to the object to decrement the ref count of.</param>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void _DecrementRefCount(byte* objPtr)
        {
            GCHeader* gcHeaderPtr = (GCHeader*)(objPtr - sizeof(GCHeader));
            if (CheckSignature(gcHeaderPtr))
            {
                gcHeaderPtr->RefCount--;

                if (gcHeaderPtr->RefCount <= 0)
                {
                    FOS_System.Object obj = (FOS_System.Object)Utilities.ObjectUtilities.GetObject(objPtr);
                    if (obj._Type == (FOS_System.Type)typeof(FOS_System.Array))
                    {
                        //Decrement ref count of elements
                        FOS_System.Array arr = (FOS_System.Array)obj;
                        if (!arr.elemType.IsValueType)
                        {
                            FOS_System.Object[] objArr = (FOS_System.Object[])Utilities.ObjectUtilities.GetObject(objPtr);
                            for (int i = 0; i < arr.length; i++)
                            {
                                DecrementRefCount(objArr[i], true);
                            }
                        }
                    }

                    AddObjectToCleanup(gcHeaderPtr, objPtr);
                }
            }
        }

        /// <summary>
        /// Checks the GC header is valid by checking for the GC signature.
        /// </summary>
        /// <param name="headerPtr">A pointer to the header to check.</param>
        /// <returns>True if the signature is found and is correct.</returns>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static unsafe bool CheckSignature(GCHeader* headerPtr)
        {
            bool OK = headerPtr->Sig1 == 0x5C0EADE2U;
            OK = OK && headerPtr->Sig2 == 0x5C0EADE2U;
            OK = OK && headerPtr->Checksum == 0xB81D5BC4U;
            return OK;
        }
        /// <summary>
        /// Sets the GC signature in the specified GC header.
        /// </summary>
        /// <param name="headerPtr">A pointer to the header to set the signature in.</param>
        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void SetSignature(GCHeader* headerPtr)
        {
            headerPtr->Sig1 = 0x5C0EADE2U;
            headerPtr->Sig2 = 0x5C0EADE2U;
            headerPtr->Checksum = 0xB81D5BC4U;
        }


        [Compiler.NoDebug]
        [Compiler.NoGC]
        public static void Cleanup()
        {
            if (!GCInitialised || InsideGC)
            {
                return;
            }

            InsideGC = true;

            ObjectToCleanup* currObjToCleanupPtr = CleanupList;
            ObjectToCleanup* prevObjToCleanupPtr = null;
            while (currObjToCleanupPtr != null)
            {
                GCHeader* objHeaderPtr = currObjToCleanupPtr->objHeaderPtr;
                void* objPtr = currObjToCleanupPtr->objPtr;
                if(objHeaderPtr->RefCount <= 0)
                {
                    FOS_System.Object obj = (FOS_System.Object)Utilities.ObjectUtilities.GetObject(objPtr);
                    if (obj._Type == (FOS_System.Type)typeof(FOS_System.String))
                    {
                        numStrings--;
                    }

                    Heap.Free(objPtr);

                    NumObjs--;
                }

                prevObjToCleanupPtr = currObjToCleanupPtr;
                currObjToCleanupPtr = currObjToCleanupPtr->prevPtr;
                RemoveObjectToCleanup(prevObjToCleanupPtr);
            }

            InsideGC = false;
        }

        [Compiler.NoDebug]
        [Compiler.NoGC]
        private static void AddObjectToCleanup(GCHeader* objHeaderPtr, void* objPtr)
        {
            ObjectToCleanup* newObjToCleanupPtr = (ObjectToCleanup*)Heap.Alloc((uint)sizeof(ObjectToCleanup));
            newObjToCleanupPtr->objHeaderPtr = objHeaderPtr;
            newObjToCleanupPtr->objPtr = objPtr;

            newObjToCleanupPtr->prevPtr = CleanupList;
            CleanupList->nextPtr = newObjToCleanupPtr;

            CleanupList = newObjToCleanupPtr;
        }
        [Compiler.NoDebug]
        [Compiler.NoGC]
        private static void RemoveObjectToCleanup(GCHeader* objHeaderPtr)
        {
            ObjectToCleanup* currObjToCleanupPtr = CleanupList;
            while (currObjToCleanupPtr != null)
            {
                if (currObjToCleanupPtr->objHeaderPtr == objHeaderPtr)
                {
                    RemoveObjectToCleanup(currObjToCleanupPtr);
                    return;
                }
                currObjToCleanupPtr = currObjToCleanupPtr->prevPtr;
            }
        }
        [Compiler.NoDebug]
        [Compiler.NoGC]
        private static void RemoveObjectToCleanup(ObjectToCleanup* objToCleanupPtr)
        {
            ObjectToCleanup* prevPtr = objToCleanupPtr->prevPtr;
            ObjectToCleanup* nextPtr = objToCleanupPtr->nextPtr;
            prevPtr->nextPtr = nextPtr;
            nextPtr->prevPtr = prevPtr;

            if(CleanupList == objToCleanupPtr)
            {
                CleanupList = prevPtr;
            }
            
            Heap.Free(objToCleanupPtr);
        }
    }
    
    /// <summary>
    /// Represents the GC header that is put in memory in front of every object so the GC can manage the object.
    /// </summary>
    public struct GCHeader
    {
        /// <summary>
        /// The first 4 bytes of the GC signature.
        /// </summary>
        public uint Sig1;
        /// <summary>
        /// The second 4 bytes of the GC signature.
        /// </summary>
        public uint Sig2;
        /// <summary>
        /// A checksum value.
        /// </summary>
        public UInt32 Checksum;

        /// <summary>
        /// The current reference count for the object associated with this header.
        /// </summary>
        public uint RefCount;
    }

    public unsafe struct ObjectToCleanup
    {
        public void* objPtr;
        public GCHeader* objHeaderPtr;
        public ObjectToCleanup* prevPtr;
        public ObjectToCleanup* nextPtr;
    }
}
