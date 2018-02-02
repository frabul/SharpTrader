using System;

namespace SharpTrader
{
    public interface ISignalNavigator
    {
        double this[int i] { get; }
        event Action OnNewSample;
        int Count { get; }
        bool EndOfSerie { get; }
        int Position { get; } 
        bool Next();
        void SeekFirst();
        void SeekLast();
    }
 
   
}