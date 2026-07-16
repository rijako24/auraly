namespace MimosBabySpa.Domain.Enums;

public enum ProductAliasScope { Business = 0, Customer = 1 }
public enum ProductAliasKind { Alias = 0, Keyword = 1, Misspelling = 2 }
public enum ProductAliasResolutionMode { SuggestOnly = 0, AutoResolve = 1 }
public enum ProductAliasSource { Manual = 0, Imported = 1, Learned = 2 }
public enum ProductAliasStatus { Pending = 0, Active = 1, Rejected = 2 }
