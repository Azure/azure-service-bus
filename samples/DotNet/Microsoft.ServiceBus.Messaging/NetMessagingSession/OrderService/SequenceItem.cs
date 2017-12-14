//   
//   Copyright © Microsoft Corporation, All Rights Reserved
// 
//   Licensed under the Apache License, Version 2.0 (the "License"); 
//   you may not use this file except in compliance with the License. 
//   You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0 
// 
//   THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//   OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
//   ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
//   PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
// 
//   See the Apache License, Version 2.0 for the specific language
//   governing permissions and limitations under the License. 

namespace NetMessagingSessionService
{
    using System.Runtime.Serialization;

    [DataContract(Namespace = "")]
    public class SequenceItem
    {
        [DataMember] public string ItemId;
        [DataMember] public int Quantity;

        public SequenceItem(string itemId)
            : this(itemId, 1)
        {
        }

        public SequenceItem(string itemId, int quantity)
        {
            this.ItemId = itemId;
            this.Quantity = quantity;
        }
    }
}