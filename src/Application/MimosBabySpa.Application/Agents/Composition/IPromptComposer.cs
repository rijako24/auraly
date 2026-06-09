namespace MimosBabySpa.Application.Agents.Composition;

public interface IPromptComposer
{
    string Compose(PromptCompositionInput input);
}
