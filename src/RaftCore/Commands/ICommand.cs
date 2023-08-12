namespace RaftCore.Commands;

public interface ICommand
{
    Task<object> ExecuteAsync(object context);
}