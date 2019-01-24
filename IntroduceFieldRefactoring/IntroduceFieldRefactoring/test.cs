using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntroduceFieldRefactoring
{
	public class test
	{
		public string test1;
		public string test2;
		private readonly IFormatProvider _formProvider;

		public test(IFormatProvider formProvider, IServiceProvider service)
		{
			//Guard.ArgumentNotNull(formProvider, nameof(formProvider));
			_formProvider = formProvider;
		}
	}
}
