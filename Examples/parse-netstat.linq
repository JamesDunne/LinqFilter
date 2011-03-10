// Run netstat -na and pipe it through this script to find number of connections per foreign address.
from line in lines.Skip(4)
where line.Length >= 32
where line != "  Proto  Local Address          Foreign Address        State"
let foreignaddrRaw = line.Substring(32, 23)
let foreignaddr = foreignaddrRaw.TrimEnd()
let ipaddr = foreignaddr.Split(':')[0]
group ipaddr by ipaddr into addressGroup
let count = addressGroup.Count()
orderby count descending
//select addressGroup.Key + "\t" + count
select addressGroup.Key + new string(' ', 20 - addressGroup.Key.Length) + count