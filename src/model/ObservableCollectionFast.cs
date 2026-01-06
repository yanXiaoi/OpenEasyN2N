using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace OpenEasyN2N.model;

public class ObservableCollectionFast<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> collection)
    {
        // 屏蔽通知
        this.CheckReentrancy();
        this.Items.Clear();
        foreach (var item in collection)
        {
            this.Items.Add(item);
        }
        // 发送一次性通知，告诉 UI：集合已经大变样了，请刷新全部
        this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
    /**
     * 增量更新，keySelector 是一个函数，用于从对象中获取唯一的键值
     */
    public void UpdateIncremental(IEnumerable<T> newList, Func<T, string> keySelector)
    {
        // 将新数据转为字典，方便快速查询
        var newDict = newList.ToDictionary(keySelector);

        // 删除旧列表中不再存在的数据（从后往前删，避免索引偏移）
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var key = keySelector(Items[i]);
            if (!newDict.ContainsKey(key))
            {
                this.RemoveAt(i);
            }
        }
        // 更新已存在的项，或添加新项
        var oldKeys = Items.Select(keySelector).ToHashSet();
        foreach (var newItem in newList)
        {
            var key = keySelector(newItem);
            if (!oldKeys.Contains(key))
                this.Add(newItem); // 添加新发现的节点
        }
    }
}