namespace Bolok;

// 미리 잡아 둔 자리에 넣고 빼는 목록: 하는 동안 메모리를 새로 잡지 않는다. 넣은 차례가 그대로 남는다 (겹친 그림의 위아래가 바뀌어 깜빡이지 않게).
// 지우기: 남길 것만 앞에서부터 다시 채우고 Trim(남긴 수)
sealed class Bag<T> where T : struct
{
    T[] items;
    public int Count { get; private set; }
    public Bag(int room) => items = new T[room];
    public bool Full => Count == items.Length;   // 다 찼다 (넣으면 자리를 두 배로 늘린다: 한도를 두려면 넣기 전에 본다)
    public ref T this[int i] => ref items[i];
    public ref T Add()
    {
        if (Count == items.Length) Array.Resize(ref items, Count * 2);
        return ref items[Count++];
    }
    public void Trim(int count) => Count = count;
}
