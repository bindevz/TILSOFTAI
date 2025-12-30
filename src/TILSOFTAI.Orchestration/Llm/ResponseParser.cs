using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TILSOFTAI.Orchestration.Llm
{
    public sealed class ResponseParser
    {

    }

    public sealed class ResponseContractException : Exception
    {
        public ResponseContractException(string message) : base(message)
        {
        }
    }
}
