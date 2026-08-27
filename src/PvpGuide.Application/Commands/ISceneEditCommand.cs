using PvpGuide.Domain;

namespace PvpGuide.Application.Commands;

internal interface ISceneEditCommand
{
    bool Execute(SceneDocument document);

    bool Undo(SceneDocument document);
}
