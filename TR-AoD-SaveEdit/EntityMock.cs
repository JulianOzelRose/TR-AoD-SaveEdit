using System.Collections.Generic;

namespace TR_AoD_SaveEdit
{
    public class EntityMock
    {
        public int BaseOffset { get; set; }
        public Dictionary<int, int> Substructures { get; set; } = new Dictionary<int, int>();
        public int? APB_Loop_Counter { get; set; }
        public int? EntityType { get; set; }
        public int ID { get; set; }
        public bool IsPlayable { get; set; }
        public string Name { get; set; }

        public EntityMock(int baseOffset)
        {
            BaseOffset = baseOffset;
        }

        public void AddSubstructure(int offset, int value)
        {
            Substructures[offset] = value;
        }

        public void SetAPBLoopCounter(int apbLoopCounter)
        {
            APB_Loop_Counter = apbLoopCounter;
        }

        public void SetActorDetails(int id, bool isPlayable, string name)
        {
            ID = id;
            IsPlayable = isPlayable;
            Name = name;
        }
    }

    public class GmxObjectInfo
    {
        public string Name { get; set; }   // GMX object name
        public int Index { get; set; }     // Index within gmapGMXCur + 0x548
    }
}
