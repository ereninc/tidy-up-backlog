using System.Threading.Tasks;

namespace EXW.SaveSystem
{
    public interface ISaveFileStore
    {
        Task<SaveOperationResult> WriteAsync(SaveSlotMetadata metadata, SaveDocument document);
        Task<SaveReadResult> ReadAsync(string slotId);
        Task<SaveCatalogResult> ListAsync();
        Task<SaveOperationResult> DeleteAsync(string slotId);
    }
}
