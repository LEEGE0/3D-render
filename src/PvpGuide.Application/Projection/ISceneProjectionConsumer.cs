using PvpGuide.Domain;

namespace PvpGuide.Application.Projection;

public interface ISceneProjectionConsumer
{
    void Apply(SceneSnapshot snapshot);
}
