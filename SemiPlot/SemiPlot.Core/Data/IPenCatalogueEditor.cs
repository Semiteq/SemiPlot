using FluentResults;

namespace SemiPlot.Core.Data;

/// <summary>
/// Everything the pen editor may do to the catalogue. It never adds a pen by hand, deletes one or changes
/// an id: the key is the SCADA variable number.
/// </summary>
public interface IPenCatalogueEditor
{
	Task<Result<PenCatalogue>> ReadAsync();

	Task<Result<int>> RegisterNewPensAsync();

	Task<Result> ChangeAsync(StoredPen pen, PenSettingChange change);

	Task<Result<int>> CreateGroupAsync(string name);

	Task<Result> RenameGroupAsync(StoredGroup group, string name);

	Task<Result> DeleteGroupAsync(StoredGroup group);

	Task<Result> SetMembershipAsync(StoredPen pen, StoredGroup group, bool isMember);
}
