using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 通用 UI 列表高性能增量比对刷新工具 (Generic In-place DiffUtil for ObservableCollection)
    /// 适用于所有 ListView/GridView/ItemsControl 列表的高性能增量同步
    /// 优势：消除全量清空与重建带来的界面白屏闪烁、已加载图片二次闪烁与滚动条跳跃
    /// </summary>
    public static class DiffUtil
    {
        /// <summary>
        /// 将新列表数据原地增量同步至现有的 ObservableCollection
        /// </summary>
        /// <typeparam name="T">列表项数据类型</typeparam>
        /// <typeparam name="TKey">列表项唯一标识类型 (如 string 或 long)</typeparam>
        /// <param name="target">当前绑定的 ObservableCollection 实例</param>
        /// <param name="newList">最新从网络或数据源获取的新列表</param>
        /// <param name="keySelector">提取项唯一键的委托 (例如 item => item.Id 或 item => item.ChatId + "_" + item.ChatType)</param>
        /// <param name="updateAction">已有项属性原地更新委托 (例如 (targetItem, newItem) => targetItem.UpdateFrom(newItem))</param>
        public static void ApplyDiff<T, TKey>(
            ObservableCollection<T> target,
            IList<T> newList,
            Func<T, TKey> keySelector,
            Action<T, T> updateAction = null)
        {
            if (target == null || keySelector == null) return;

            // 1. 新列表为空：直接清空
            if (newList == null || newList.Count == 0)
            {
                target.Clear();
                return;
            }

            // 2. 目标列表为空：直接按序批量添加
            if (target.Count == 0)
            {
                for (int i = 0; i < newList.Count; i++)
                {
                    target.Add(newList[i]);
                }
                return;
            }

            // 3. 构建新列表的键集合，用于 O(1) 快速判定淘汰项
            var newKeySet = new HashSet<TKey>();
            for (int i = 0; i < newList.Count; i++)
            {
                var item = newList[i];
                if (item != null)
                {
                    TKey key = keySelector(item);
                    if (key != null && !newKeySet.Contains(key))
                    {
                        newKeySet.Add(key);
                    }
                }
            }

            // 4. 第一轮：从后往前移除 target 中已不存在于 newList 的旧项
            for (int i = target.Count - 1; i >= 0; i--)
            {
                var cur = target[i];
                if (cur == null)
                {
                    target.RemoveAt(i);
                    continue;
                }

                TKey key = keySelector(cur);
                if (key == null || !newKeySet.Contains(key))
                {
                    target.RemoveAt(i);
                }
            }

            // 5. 第二轮：按 newList 顺序对齐、原地更新属性或插入新增项
            for (int targetIdx = 0; targetIdx < newList.Count; targetIdx++)
            {
                var newItem = newList[targetIdx];
                if (newItem == null) continue;

                TKey newKey = keySelector(newItem);
                if (newKey == null) continue;

                // 在 target 中查找该项当前所在位置
                int currentIdx = -1;
                for (int j = 0; j < target.Count; j++)
                {
                    var targetItem = target[j];
                    if (targetItem != null)
                    {
                        TKey tKey = keySelector(targetItem);
                        if (object.Equals(tKey, newKey))
                        {
                            currentIdx = j;
                            break;
                        }
                    }
                }

                if (currentIdx == -1)
                {
                    // 新增项：插入到指定索引
                    if (targetIdx < target.Count)
                    {
                        target.Insert(targetIdx, newItem);
                    }
                    else
                    {
                        target.Add(newItem);
                    }
                }
                else
                {
                    // 已有项：原地更新属性（保留头像位图等 UI 状态）
                    if (updateAction != null)
                    {
                        updateAction(target[currentIdx], newItem);
                    }

                    // 若位置发生变化，执行原子移动
                    if (currentIdx != targetIdx && targetIdx < target.Count)
                    {
                        target.Move(currentIdx, targetIdx);
                    }
                }
            }
        }

        /// <summary>
        /// 针对实时推送通知或置顶项操作，将单个项增量更新或移至列表顶部
        /// </summary>
        /// <typeparam name="T">列表项数据类型</typeparam>
        /// <typeparam name="TKey">唯一标识类型</typeparam>
        /// <param name="target">目标列表</param>
        /// <param name="newItem">新项</param>
        /// <param name="keySelector">唯一键提取器</param>
        /// <param name="updateAction">原地属性合并委托</param>
        public static void UpsertAndMoveToTop<T, TKey>(
            ObservableCollection<T> target,
            T newItem,
            Func<T, TKey> keySelector,
            Action<T, T> updateAction = null)
        {
            if (target == null || newItem == null || keySelector == null) return;

            TKey newKey = keySelector(newItem);
            if (newKey == null) return;

            int foundIdx = -1;
            for (int i = 0; i < target.Count; i++)
            {
                var item = target[i];
                if (item != null && object.Equals(keySelector(item), newKey))
                {
                    foundIdx = i;
                    break;
                }
            }

            if (foundIdx >= 0)
            {
                if (updateAction != null)
                {
                    updateAction(target[foundIdx], newItem);
                }

                if (foundIdx > 0)
                {
                    target.Move(foundIdx, 0);
                }
            }
            else
            {
                target.Insert(0, newItem);
            }
        }
    }
}
