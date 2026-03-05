using System;

namespace TR_AoD_SaveEdit
{
    public class Savegame
    {
        public string DisplayName { get; set; }
        public string FileName { get; set; }
    }

    public struct InventoryItem
    {
        public ushort ClassId { get; set; }
        public Int32 Type { get; set; }
        public Int32 Quantity { get; set; }

        public InventoryItem(ushort classId, int type, int quantity)
        {
            ClassId = classId;
            Type = type;
            Quantity = quantity;
        }

        public override string ToString()
        {
            return $"ClassId: 0x{ClassId:X}, Type: {Type}, Quantity: {Quantity}";
        }
    }
}