-- The learner's own words and self-rating that go with the proof, matching the programme sheet
-- (columns "What You Learn" and "Rate (1-5)").
alter table submissions add column if not exists learned text check (learned is null or char_length(learned) <= 2000);
alter table submissions add column if not exists rating smallint check (rating is null or rating between 1 and 5);
