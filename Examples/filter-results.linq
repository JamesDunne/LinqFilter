from line in lines
where line.Length > 0
let cols = line.Split('\t')
where cols.Length >= 11
select line