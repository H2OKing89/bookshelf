import { useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes, kinds } from 'Helpers/Props';
import { fetchBooks } from 'Store/Actions/bookActions';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getErrorMessage from 'Utilities/Object/getErrorMessage';

const empty = {
  kind: 'author', region: 'us', asin: '', name: '', alternateAsins: '', aliases: '',
  originalTitle: '', originalSubtitle: '', replacementTitle: '', sourceUrl: '', reason: '', enabled: true
};
const typeOptions = [{ key: 'author', value: 'Author identity' }, { key: 'title', value: 'Title correction' }, { key: 'titleprotection', value: 'Title/subtitle protection' }, { key: 'narrator', value: 'Narrator alias' }];
const regionOptions = ['au', 'ca', 'de', 'es', 'fr', 'in', 'it', 'jp', 'us', 'uk'].map((region) => ({ key: region, value: region.toUpperCase() }));
const statuses = { enabled: 'Enabled', disabled: 'Disabled', needsReview: 'Needs review', notInLibrary: 'Not in library' };

function overrideName(item) {
  if (item.kind === 'title') {
    return item.replacementTitle;
  }

  if (item.kind === 'titleprotection') {
    return `Protected: ${item.originalTitle || item.originalSubtitle}`;
  }

  return item.name;
}

function MetadataOverrides() {
  const dispatch = useDispatch();
  const [items, setItems] = useState([]);
  const [form, setForm] = useState(null);
  const [error, setError] = useState(null);
  const [isSaving, setIsSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    const request = createAjaxRequest({ url: '/metadataoverride' });
    request.request.done(setItems).fail((xhr) => {
      if (!xhr.aborted) {
        setError(getErrorMessage(xhr, 'Unable to load metadata overrides.'));
      }
    });
    return request.abortRequest;
  }, []);

  const edit = (item) => {
    setForm({ ...empty, ...item, alternateAsins: (item.alternateAsins || []).join('\n'), aliases: (item.aliases || []).join('\n') });
    setError(null);
    setSaved(false);
  };
  const onChange = ({ name, value }) => setForm({ ...form, [name]: value });
  const save = () => {
    setIsSaving(true);
    setError(null);
    const payload = {
      ...form,
      alternateAsins: form.alternateAsins.split('\n').map((s) => s.trim()).filter(Boolean),
      aliases: form.aliases.split('\n').map((s) => s.trim()).filter(Boolean)
    };
    createAjaxRequest({
      url: form.id ? `/metadataoverride/${form.id}` : '/metadataoverride',
      method: form.id ? 'PUT' : 'POST',
      dataType: 'json',
      data: JSON.stringify(payload)
    }).request.done((item) => {
      setItems((previous) => [...previous.filter((row) => row.id !== item.id), item]);
      setForm(null);
      setSaved(true);
      dispatch(fetchBooks({}));
    }).fail((xhr) => {
      const validation = Array.isArray(xhr.responseJSON) ? xhr.responseJSON.map((e) => e.errorMessage).join(' ') : null;
      setError(validation || getErrorMessage(xhr, 'Unable to save metadata override.'));
    }).always(() => setIsSaving(false));
  };

  const remove = () => {
    setIsSaving(true);
    setError(null);
    createAjaxRequest({ url: `/metadataoverride/${form.id}`, method: 'DELETE' }).request.done(() => {
      setItems((previous) => previous.filter((item) => item.id !== form.id));
      setForm(null);
      setSaved(true);
      dispatch(fetchBooks({}));
    }).fail((xhr) => {
      setError(getErrorMessage(xhr, 'Unable to delete metadata override.'));
    }).always(() => setIsSaving(false));
  };

  let specificFields = [];
  if (form?.kind === 'title') {
    specificFields = [
      ['originalTitle', 'Exact provider title', inputTypes.TEXT],
      ['replacementTitle', 'Corrected title', inputTypes.TEXT, { helpText: 'Used for display labels. The original provider title remains stored.' }]
    ];
  } else if (form?.kind === 'titleprotection') {
    specificFields = [
      ['originalTitle', 'Exact provider title to protect', inputTypes.TEXT, { helpText: 'Leave blank to protect only the subtitle. Protected text stays intact while the provider field matches exactly.' }],
      ['originalSubtitle', 'Exact provider subtitle to protect', inputTypes.TEXT, { helpText: 'Provide the title, subtitle, or both. A provider change requires a new review.' }]
    ];
  } else if (form) {
    specificFields = [
      ['name', form.kind === 'author' ? 'Canonical author name' : 'Canonical narrator name', inputTypes.TEXT],
      ...(form.kind === 'author' ? [['alternateAsins', 'Alternate author ASINs', inputTypes.TEXT_AREA, { helpText: 'One ASIN per line. Add only profiles verified to belong to the same author.' }]] : []),
      ['aliases', 'Other spellings or aliases', inputTypes.TEXT_AREA, { helpText: 'One name per line. Add only names verified to identify the same person.' }]
    ];
  }

  const fields = form ? [
    ['kind', 'Correction type', inputTypes.SELECT, { values: typeOptions, isDisabled: !!form.id }],
    ['region', 'Marketplace', inputTypes.SELECT, { values: regionOptions }],
    ...(form.kind === 'narrator' ? [] : [['asin', form.kind === 'author' ? 'Canonical author ASIN' : 'Recording ASIN', inputTypes.TEXT]]),
    ...specificFields,
    ['sourceUrl', 'Evidence URL', inputTypes.TEXT],
    ['reason', 'Reason', inputTypes.TEXT_AREA],
    ['enabled', 'Enabled', inputTypes.CHECK]
  ] : [];

  return (
    <FieldSet legend="Metadata overrides">
      <p>
        Keep verified author identities, narrator aliases, title corrections, and title/subtitle protections here. Protections preserve meaningful text that would otherwise match a cleanup rule. Refresh affected authors after changing an author identity.
      </p>
      {
        error && <Alert kind={kinds.DANGER}>{error}</Alert>
      }
      {
        saved && <Alert kind={kinds.SUCCESS}>Metadata overrides updated.</Alert>
      }
      {
        items.map((item) => (
          <div key={item.id}>
            <Button isDisabled={isSaving} onPress={() => edit(item)}>
              {overrideName(item)} ({item.region.toUpperCase()}) — {statuses[item.status]}
            </Button>
            {
              item.status === 'needsReview' &&
                <Alert kind={kinds.WARNING}>
                  Provider text changed. Current title: “{item.observedTitle}”; current subtitle: “{item.observedSubtitle || '(none)'}”. Changed fields no longer receive this correction or protection. Review the evidence before updating it.
                </Alert>
            }
          </div>
        ))
      }
      <Button isDisabled={isSaving} onPress={() => { setForm({ ...empty }); setSaved(false); setError(null); }}>
        Add correction
      </Button>
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
                    value={form[name] || (name === 'enabled' ? false : '')}
                    isDisabled={isSaving}
                    onChange={onChange}
                    {...options}
                  />
                </FormGroup>
              ))
            }
            <Button kind={kinds.PRIMARY} isDisabled={isSaving} onPress={save}>Save correction</Button>
            {form.id && <Button isDisabled={isSaving} onPress={remove}>Delete override</Button>}
            <Button isDisabled={isSaving} onPress={() => setForm(null)}>Cancel</Button>
          </div>
      }
    </FieldSet>
  );
}

export default MetadataOverrides;
