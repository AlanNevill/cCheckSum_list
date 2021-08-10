use pops;

select count(*) from checksum where SCreateDateTime = 'Date not found';

select count(*) from checksum;

select count(*) from checksum where CreateDateTime is null;

select * from checksum where CreateDateTime is null;

select fileSize,count(*) from checksum 
--where createdatetime is not null
group by filesize having count(*)>1 order by count(*) desc;

select * from CheckSum where createdatetime = '2006-12-23 23:47:50.000';
select * from CheckSum where filesize = 59020;