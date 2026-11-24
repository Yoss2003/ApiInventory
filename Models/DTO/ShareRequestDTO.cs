namespace InventoryAPI.Models.DTO
{
    public class ShareRequestDTO
    {
        public int InventoryId { get; set; }
        public string GuestIdentifier { get; set; } = string.Empty;
        public int? GuestUserId { get; set; }

        public SharedInventory.AccessMode AccessLevel { get; set; }
    }
}
