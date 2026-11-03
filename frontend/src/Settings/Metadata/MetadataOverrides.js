import React, { useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes, kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getErrorMessage from 'Utilities/Object/getErrorMessage';

const empty = {
  kind: 'author', region: 'us', asin: '', name: '', alternateAsins: '', aliases: '',
  originalTitle: '', replacementTitle: '', sourceUrl: '', reason: '', enabled: true
};
const typeOptions = [{ key: 'author', value: 'Author identity' }, { key: 'title', value: 'Title correction' }];
const regionOptions = ['au', 'ca', 'de', 'es', 'fr', 'in', 'it', 'jp', 'us', 'uk'].map((region) => ({ key: region, value: region.toUpperCase() }));
const statuses = { enabled: 'Enabled', disabled: 'Disabled', needsReview: 'Needs review', notInLibrary: 'Not in library' };

function MetadataOverrides() {
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
    setForm({ ...item, alternateAsins: item.alternateAsins.join('\n'), aliases: item.aliases.join('\n') });
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
    }).fail((xhr) => {
      const validation = Array.isArray(xhr.responseJSON) ? xhr.responseJSON.map((e) => e.errorMessage).join(' ') : null;
      setError(validation || getErrorMessage(xhr, 'Unable to save metadata override.'));
    }).always(() => setIsSaving(false));
  };

  const fields = form ? [
    ['kind', 'Correction type', inputTypes.SELECT, { values: typeOptions, isDisabled: !!form.id }],
    ['region', 'Marketplace', inputTypes.SELECT, { values: regionOptions }],
    ['asin', form.kind === 'author' ? 'Canonical author ASIN' : 'Recording ASIN', inputTypes.TEXT],
    ...(form.kind === 'author' ? [
      ['name', 'Canonical author name', inputTypes.TEXT],
      ['alternateAsins', 'Alternate author ASINs', inputTypes.TEXT_AREA, { helpText: 'One ASIN per line. Add only profiles verified to belong to the same author.' }],
      ['aliases', 'Other author spellings', inputTypes.TEXT_AREA, { helpText: 'One name per line. Names without an author ASIN can use these spellings.' }]
    ] : [
      ['originalTitle', 'Exact provider title', inputTypes.TEXT],
      ['replacementTitle', 'Corrected title', inputTypes.TEXT, { helpText: 'Used for display labels. The original provider title remains stored.' }]
    ]),
    ['sourceUrl', 'Evidence URL', inputTypes.TEXT],
    ['reason', 'Reason', inputTypes.TEXT_AREA],
    ['enabled', 'Enabled', inputTypes.CHECK]
  ] : [];

  return (
    <FieldSet legend="Metadata overrides">
      <p>
        Keep verified author identities and title corrections here. Refresh affected authors after changing an author identity. Title corrections apply to display labels immediately after the page reloads.
      </p>
      {
        error && <Alert kind={kinds.DANGER}>{error}</Alert>
      }
      {
        saved && <Alert kind={kinds.SUCCESS}>Correction saved.</Alert>
      }
      {
        items.map((item) => (
          <div key={item.id}>
            <Button isDisabled={isSaving} onPress={() => edit(item)}>
              {item.kind === 'author' ? item.name : item.replacementTitle} ({item.region.toUpperCase()}) — {statuses[item.status]}
            </Button>
            {
              item.status === 'needsReview' &&
                <Alert kind={kinds.WARNING}>
                  The provider title changed to “{item.observedTitle}”. This correction no longer applies. Review its evidence before updating it.
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
            <Button isDisabled={isSaving} onPress={() => setForm(null)}>Cancel</Button>
          </div>
      }
    </FieldSet>
  );
}

export default MetadataOverrides;
