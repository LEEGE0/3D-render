namespace PvpGuide.Application.Projection;

public interface ISceneProjectionConsumer
{
    void Apply(SceneProjectionFrame frame);
}
