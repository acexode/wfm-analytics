"""Executable specification checks; not a production aggregation implementation."""
import copy
import csv
import json
import unittest
from datetime import datetime, time, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return json.loads((ROOT / path).read_text())


def instant(value):
    result = datetime.fromisoformat(value.replace('Z', '+00:00'))
    if result.utcoffset() is None:
        raise ValueError('Explicit UTC offset required')
    return result.timestamp()


def reconcile(case):
    zone = ZoneInfo(case['timezone'])
    day = datetime.fromisoformat(case['report_date']).date()
    lo = datetime.combine(day, time(), zone).timestamp()
    hi = datetime.combine(day + timedelta(days=1), time(), zone).timestamp()
    schedule = [(max(lo, instant(x['start'])), min(hi, instant(x['end']))) for x in case['schedule']]
    observations = [(instant(x['start']), instant(x['end']), x['state'], x['source']) for x in case['observations']]
    annotations = [(instant(x['start']), instant(x['end'])) for x in case['annotations'] if x['approved']]
    boundaries = sorted({lo, hi, *(v for pair in schedule + annotations for v in pair), *(v for row in observations for v in row[:2])})
    result = dict.fromkeys(['eligible_seconds', 'active_seconds', 'inactive_seconds', 'unknown_seconds', 'conflict_seconds', 'approved_non_desktop_seconds', 'telemetry_seconds'], 0)
    for start, end in zip(boundaries, boundaries[1:]):
        if not lo <= start < hi or not any(a <= start < b for a, b in schedule):
            continue
        duration = int(end - start)
        result['eligible_seconds'] += duration
        observed = [(state, source) for a, b, state, source in observations if a <= start < b and state != 'detail_unavailable']
        if observed:
            result['telemetry_seconds'] += duration
        if any(a <= start < b for a, b in annotations):
            category = 'approved_non_desktop'
        elif len({source for _, source in observed}) > 1:
            category = 'conflict'
        elif any(state in ('inactive', 'locked') for state, _ in observed):
            category = 'inactive'
        elif observed:
            category = 'active'
        else:
            category = 'unknown'
        result[category + '_seconds'] += duration
    return result


def validate_batch_semantics(batch):
    for event in batch['events']:
        duration = (instant(event['bucket_end']) - instant(event['bucket_start'])) * 1000
        if not 0 < duration <= 60000 or instant(event['bucket_start']) % 60:
            raise ValueError('Bucket must be minute aligned and bounded')
        previous = 0
        for item in event['slices']:
            if not previous <= item['start_offset_ms'] < item['end_offset_ms'] <= duration:
                raise ValueError('Slices must be ordered and nonoverlapping')
            previous = item['end_offset_ms']
            if item['state'] == 'detail_unavailable' and (item['application_id'] is not None or not event['coarsened'] or previous != duration):
                raise ValueError('Unavailable suffix must have no application detail')


def validate_report_semantics(report):
    for employee in report['employees']:
        eligible = employee['eligible_seconds']
        categories = employee['categories']
        if sum(categories.values()) != eligible:
            raise ValueError('Categories must reconcile to eligible time')
        coverage = employee['telemetry_coverage']['value']
        if eligible == 0:
            if coverage is not None:
                raise ValueError('No denominator requires unavailable coverage')
        else:
            if coverage is None:
                raise ValueError('Eligible time requires coverage')
            minimum = sum(categories[k] for k in ('active_seconds', 'inactive_seconds', 'conflict_seconds'))
            maximum = minimum + categories['approved_non_desktop_seconds']
            if not minimum - 1e-8 <= coverage * eligible <= maximum + 1e-8:
                raise ValueError('Coverage inconsistent with observed categories')


def csv_errors(feed, path):
    with path.open() as handle:
        rows = list(csv.DictReader(handle))
    with (ROOT / f'fixtures/imports/{feed}-template.csv').open() as handle:
        headers = next(csv.reader(handle))
    errors, seen, intervals = [], set(), []
    for number, row in enumerate(rows, 2):
        try:
            if list(row) != headers or any(not row.get(key) for key in headers):
                raise ValueError('Required column/value missing')
            if row['source_id'] in seen:
                raise ValueError('Duplicate source key')
            seen.add(row['source_id'])
            if feed == 'schedules':
                ZoneInfo(row['timezone'])
                start, end = instant(row['start']), instant(row['end'])
                if start >= end or any(employee == row['employee_id'] and start < b and a < end for employee, a, b in intervals):
                    raise ValueError('Invalid or overlapping schedule')
                intervals.append((row['employee_id'], start, end))
            elif feed == 'roster':
                if datetime.fromisoformat(row['effective_start']) >= datetime.fromisoformat(row['effective_end']):
                    raise ValueError('Invalid assignment interval')
            else:
                datetime.fromisoformat(row['report_date'])
                if feed == 'output':
                    if int(row['completed_count']) < 0 or not 0 <= int(row['passed_count']) <= int(row['reviewed_count']):
                        raise ValueError('Invalid quality counts')
                else:
                    ZoneInfo(row['timezone'])
                    if int(row['approved_seconds']) < 0:
                        raise ValueError('Negative approved hours')
        except (ValueError, KeyError, TypeError):
            errors.append(number)
    return errors


class Contracts(unittest.TestCase):
    def test_schema_examples_and_forbidden_fields(self):
        for name in ('daily-report', 'activity-batch', 'activity-batch-response'):
            schema = read(f'contracts/{name}.schema.json')
            Draft202012Validator.check_schema(schema)
            validator = Draft202012Validator(schema, format_checker=FormatChecker())
            payload = read(f'fixtures/{name}.json')
            validator.validate(payload)
            payload['window_title'] = 'forbidden'
            self.assertTrue(list(validator.iter_errors(payload)))

    def test_reconciliation_reference_cases(self):
        cases = read('fixtures/reconciliation.json')
        self.assertGreaterEqual(len(cases), 8)
        for case in cases:
            with self.subTest(case=case['id']):
                actual = reconcile(case)
                self.assertEqual(case['expected'], actual)
                self.assertEqual(actual['eligible_seconds'], sum(v for k, v in actual.items() if k not in ('eligible_seconds', 'telemetry_seconds')))

    def test_slice_semantics(self):
        batch = read('fixtures/activity-batch.json')
        validate_batch_semantics(batch)
        invalid = copy.deepcopy(batch)
        invalid['events'][0]['slices'] *= 2
        with self.assertRaises(ValueError):
            validate_batch_semantics(invalid)
        invalid = copy.deepcopy(batch)
        invalid['events'][0]['bucket_end'] = '2026-09-13T09:02:00Z'
        with self.assertRaises(ValueError):
            validate_batch_semantics(invalid)

    def test_csv_examples(self):
        for feed in ('roster', 'schedules', 'output', 'approved-hours'):
            for validity in ('valid', 'invalid'):
                with self.subTest(feed=feed, validity=validity):
                    errors = csv_errors(feed, ROOT / f'fixtures/imports/{feed}-{validity}.csv')
                    self.assertEqual(validity == 'valid', not errors)
        self.assertEqual([3], csv_errors('schedules', ROOT / 'fixtures/imports/schedules-overlap-invalid.csv'))
        self.assertEqual([3], csv_errors('output', ROOT / 'fixtures/imports/output-duplicate-invalid.csv'))

    def test_report_invalid_examples(self):
        report = read('fixtures/daily-report.json')
        validate_report_semantics(report)
        validator = Draft202012Validator(read('contracts/daily-report.schema.json'), format_checker=FormatChecker())
        for mutation in read('fixtures/report-invalid-mutations.json'):
            with self.subTest(name=mutation['name']):
                invalid = copy.deepcopy(report)
                target = invalid
                for key in mutation['path'][:-1]:
                    target = target[key]
                target[mutation['path'][-1]] = mutation['value']
                if mutation['layer'] == 'schema':
                    self.assertTrue(list(validator.iter_errors(invalid)))
                else:
                    with self.assertRaises(ValueError):
                        validate_report_semantics(invalid)
        invalid = copy.deepcopy(report)
        invalid['employees'][0]['telemetry_coverage']['value'] = 0.5
        with self.assertRaises(ValueError):
            validate_report_semantics(invalid)

    def test_openapi_response_reference(self):
        specification = read('contracts/openapi.json')
        references = (
            (specification['paths']['/api/v1/teams/{teamId}/daily']['get']['responses']['200']['content']['application/json']['schema']['$ref'], 'daily-report'),
            (specification['paths']['/api/v1/activity/batches']['post']['requestBody']['content']['application/json']['schema']['$ref'], 'activity-batch'),
            (specification['paths']['/api/v1/activity/batches']['post']['responses']['200']['content']['application/json']['schema']['$ref'], 'activity-batch-response'),
        )
        for reference, fixture in references:
            with self.subTest(fixture=fixture):
                schema = read('contracts/' + reference.removeprefix('./'))
                Draft202012Validator(schema, format_checker=FormatChecker()).validate(read(f'fixtures/{fixture}.json'))

    def test_nested_activity_sensitive_field(self):
        payload = read('fixtures/activity-batch.json')
        payload['events'][0]['slices'][0]['url'] = 'forbidden'
        validator = Draft202012Validator(read('contracts/activity-batch.schema.json'))
        self.assertTrue(list(validator.iter_errors(payload)))


if __name__ == '__main__':
    unittest.main()
