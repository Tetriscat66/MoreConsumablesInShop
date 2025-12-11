using System.Collections.Generic;

namespace MoreConsumablesInShop {
	internal static class Extensions {
		public static void AddIf<T>(this List<T> self, T item, bool cond) {
			if(cond) {
				self.Add(item);
			}
		}
	}
}
