import React, { useEffect, useRef, useState } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import { fetchBooks } from 'Store/Actions/bookActions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getErrorMessage from 'Utilities/Object/getErrorMessage';

const emptyRule = { phrase: '', labels: '', enabled: false, reason: '' };
const pageSize = 50;
const previewModes = { all: 'Preview with every rule enabled', draft: 'Unsaved draft preview', saved: 'Saved rule preview' };

function toRule(form) {
  return {
    ...form,
    labels: form.labels.split('\n').map((label) => label.trim()).filter(Boolean)
  };
}

function errorMessage(xhr, fallback) {
  const validation = Array.isArray(xhr.responseJSON) ? xhr.responseJSON.map((item) => item.errorMessage).join(' ') : null;
  return validation || getErrorMessage(xhr, fallback);
}

function MetadataCleanup() {
  const dispatch = useDispatch();
  const requests = useRef([]);
  const [items, setItems] = useState([]);
  const [form, setForm] = useState(null);
  const [bookIds, setBookIds] = useState('');
  const [error, setError] = useState(null);
  const [message, setMessage] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isPreviewing, setIsPreviewing] = useState(false);
  const [preview, setPreview] = useState(null);
  const [previewRequest, setPreviewRequest] = useState(null);

  useEffect(() => {
    const pendingRequests = requests.current;
    const request = createAjaxRequest({ url: '/metadatacleanuprule' });
    request.request.done(setItems).fail((xhr) => {
      if (!xhr.aborted) {
        setError(errorMessage(xhr, 'Unable to load title cleanup rules.'));
      }
    }).always(() => setIsLoading(false));
    return () => {
      request.abortRequest();
      pendingRequests.forEach((pending) => pending.abortRequest());
    };
  }, []);

  const edit = (item) => {
    setForm({ ...emptyRule, ...item, labels: (item.labels || []).join('\n') });
    setError(null);
    setMessage(null);
    setPreview(null);
  };

  const save = () => {
    setIsSaving(true);
    setError(null);
    setMessage(null);
    const request = createAjaxRequest({
      url: form.id ? `/metadatacleanuprule/${form.id}` : '/metadatacleanuprule',
      method: form.id ? 'PUT' : 'POST',
      dataType: 'json',
      data: JSON.stringify(toRule(form))
    });
    requests.current.push(request);
    request.request.done((item) => {
      setItems((previous) => [...previous.filter((row) => row.id !== item.id), item]);
      setForm(null);
      setPreview(null);
      setMessage('Cleanup rule saved. Library titles and labels have been refreshed.');
      dispatch(fetchBooks({}));
    }).fail((xhr) => {
      if (!xhr.aborted) {
        setError(errorMessage(xhr, 'Unable to save cleanup rule.'));
      }
    }).always(() => setIsSaving(false));
  };

  const remove = (item) => {
    setIsSaving(true);
    setError(null);
    setMessage(null);
    const request = createAjaxRequest({ url: `/metadatacleanuprule/${item.id}`, method: 'DELETE' });
    requests.current.push(request);
    request.request.done(() => {
      setItems((previous) => previous.filter((row) => row.id !== item.id));
      setForm(null);
      setPreview(null);
      setMessage('Cleanup rule deleted. Library titles and labels have been refreshed.');
      dispatch(fetchBooks({}));
    }).fail((xhr) => {
      if (!xhr.aborted) {
        setError(errorMessage(xhr, 'Unable to delete cleanup rule.'));
      }
    }).always(() => setIsSaving(false));
  };

  const loadPreview = (settings, page = 1) => {
    setIsPreviewing(true);
    setError(null);
    const request = createAjaxRequest({
      url: '/metadatacleanup/preview', method: 'POST', dataType: 'json',
      data: JSON.stringify({ rules: settings.rules, bookIds: settings.bookIds, page, pageSize })
    });
    requests.current.push(request);
    request.request.done((result) => {
      setPreview(result);
      setPreviewRequest(settings);
    }).fail((xhr) => {
      if (!xhr.aborted) {
        setError(errorMessage(xhr, 'Unable to preview title cleanup.'));
      }
    }).always(() => setIsPreviewing(false));
  };

  const startPreview = (enableAll = false) => {
    const ids = bookIds.split(/[\s,]+/).filter(Boolean);
    if (ids.some((id) => !/^\d+$/.test(id) || Number(id) <= 0)) {
      setError('Enter positive book IDs separated by commas, or leave blank to preview the library.');
      return;
    }

    let rules = items;
    if (form) {
      rules = [...items.filter((item) => item.id !== form.id), toRule(form)];
    }

    if (enableAll) {
      rules = rules.map((rule) => ({ ...rule, enabled: true }));
    }

    let mode = form ? 'draft' : 'saved';
    if (enableAll) {
      mode = 'all';
    }

    setMessage(null);
    loadPreview({ rules, bookIds: ids.length ? Array.from(new Set(ids.map(Number))) : null, mode });
  };

  const busy = isLoading || isSaving || isPreviewing;
  const previewAffected = preview?.affected ?? preview?.changed ?? 0;
  const fields = [
    ['phrase', 'Recognized phrase', inputTypes.TEXT, { helpText: 'Matches a complete label, parenthetical, or delimited segment. Words inside a meaningful title are preserved.' }],
    ['labels', 'Metadata labels', inputTypes.TEXT_AREA, { helpText: 'One label per line, such as Light Novel or LitRPG. Leave blank to remove promotional text without deriving a label.' }],
    ['reason', 'Reason', inputTypes.TEXT_AREA],
    ['enabled', 'Enabled', inputTypes.CHECK]
  ];

  return (
    <FieldSet legend="Title cleanup">
      <p>
        Remove recognized promotional labels from displayed titles, subtitles, series, and audiobook naming. Published metadata is retained. Unknown wording stays intact. Metadata labels are separate from the Tags used by indexers and download clients.
      </p>
      <p>
        New default rules start disabled. Preview them with “Preview all rules enabled” before enabling the rules you want. Previewing does not save rules or rename files. These controls save separately from the catalog settings toolbar.
      </p>
      {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
      {message && <Alert kind={kinds.SUCCESS}>{message}</Alert>}
      {isLoading && <LoadingIndicator />}
      {!isLoading && !items.length && <p>No cleanup rules configured.</p>}
      {
        items.map((item) => (
          <div key={item.id}>
            <Button isDisabled={busy} onPress={() => edit(item)}>
              {item.phrase} — {item.enabled ? 'Enabled' : 'Disabled'}{item.labels?.length ? ` → ${item.labels.join(', ')}` : ''}
            </Button>
            <Button isDisabled={busy} onPress={() => remove(item)}>Delete</Button>
          </div>
        ))
      }
      <Button isDisabled={busy} onPress={() => edit(emptyRule)}>Add rule</Button>
      {
        form &&
          <div>
            {
              fields.map(([name, label, type, options]) => (
                <FormGroup key={name}>
                  <FormLabel>{label}</FormLabel>
                  <FormInputGroup
                    type={type}
                    name={name}
                    value={form[name]}
                    isDisabled={busy}
                    onChange={({ value }) => { setForm({ ...form, [name]: value }); setPreview(null); }}
                    {...options}
                  />
                </FormGroup>
              ))
            }
            <Button kind={kinds.PRIMARY} isDisabled={busy} onPress={save}>Save rule</Button>
            <Button isDisabled={busy} onPress={() => { setForm(null); setPreview(null); }}>Cancel edit</Button>
          </div>
      }
      <FormGroup>
        <FormLabel>Preview book IDs</FormLabel>
        <FormInputGroup
          type={inputTypes.TEXT}
          name="previewBookIds"
          value={bookIds}
          isDisabled={busy}
          helpText="Optional comma-separated Bookshelf book IDs. Leave blank to sample affected books across the library."
          onChange={({ value }) => { setBookIds(value); setPreview(null); }}
        />
      </FormGroup>
      <Button isDisabled={busy} onPress={() => startPreview()}>{form ? 'Preview draft rule' : 'Preview saved rules'}</Button>
      <Button isDisabled={busy || !items.length} onPress={() => startPreview(true)}>Preview all rules enabled</Button>
      {isPreviewing && <LoadingIndicator />}
      {
        preview &&
          <div>
            <p>
              {previewModes[previewRequest.mode]}: {preview.changed} books with cleanup changes of {preview.total} books checked; {previewAffected - preview.changed} additional books with warnings. No settings or files were changed by this preview.
            </p>
            {!preview.items.length && <p>No title changes or warnings for these rules and books.</p>}
            {
              preview.items.map((item) => (
                <div key={item.bookId}>
                  <p><strong>Book {item.bookId}: {item.title}</strong></p>
                  <p>Title: {item.title} → {item.displayTitle}</p>
                  <p>Subtitle: {item.subtitle || '(none)'} → {item.displaySubtitle || '(none)'}</p>
                  <p>Series: {item.seriesTitle || '(none)'} → {item.displaySeriesTitle || '(none)'}</p>
                  <p>Metadata labels: {(item.metadataLabels || []).join(', ') || '(none)'}</p>
                  {
                    (item.changes || []).map((change, index) => (
                      <p key={`${change.field}-${index}`}>
                        {change.field}: removed “{change.removedText}”{change.reason ? ` — ${change.reason}` : ''}
                      </p>
                    ))
                  }
                  {
                    (item.warnings || []).map((warning) => <Alert key={warning} kind={kinds.WARNING}>{warning}</Alert>)
                  }
                </div>
              ))
            }
            <Button isDisabled={busy || preview.page <= 1} onPress={() => loadPreview(previewRequest, preview.page - 1)}>Previous</Button>
            <span> Page {preview.page} </span>
            <Button isDisabled={busy || preview.page * preview.pageSize >= previewAffected} onPress={() => loadPreview(previewRequest, preview.page + 1)}>Next</Button>
          </div>
      }
    </FieldSet>
  );
}

export default MetadataCleanup;
