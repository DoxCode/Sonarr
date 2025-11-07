import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import Button from 'Components/Link/Button';
import TextInput from 'Components/Form/TextInput';
import Episode from 'Episode/Episode';
import useEpisode, { EpisodeEntities } from 'Episode/useEpisode';
import useEpisodeFile from 'EpisodeFile/useEpisodeFile';
import { icons, kinds, sizes } from 'Helpers/Props';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';
import QualityProfileNameConnector from 'Settings/Profiles/Quality/QualityProfileNameConnector';
import {
  deleteEpisodeFile,
  fetchEpisodeFile,
  moveEpisodeFile,
} from 'Store/Actions/episodeFileActions';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import EpisodeAiring from './EpisodeAiring';
import EpisodeFileRow from './EpisodeFileRow';
import styles from './EpisodeSummary.css';

const COLUMNS: Column[] = [
  {
    name: 'path',
    label: () => translate('Path'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'languages',
    label: () => translate('Languages'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'customFormats',
    label: () => translate('Formats'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'customFormatScore',
    label: React.createElement(Icon, {
      name: icons.SCORE,
      title: () => translate('CustomFormatScore'),
    }),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'actions',
    label: '',
    isSortable: false,
    isVisible: true,
  },
];

interface EpisodeSummaryProps {
  seriesId: number;
  episodeId: number;
  episodeEntity: EpisodeEntities;
  episodeFileId?: number;
}

function EpisodeSummary(props: EpisodeSummaryProps) {
  const { seriesId, episodeId, episodeEntity, episodeFileId } = props;

  const dispatch = useDispatch();
  const [editingPath, setEditingPath] = useState(false);
  const [newPath, setNewPath] = useState('');
  const [isSaving, setIsSaving] = useState(false);

  const { qualityProfileId, network } = useSeries(seriesId) as Series;

  const { airDateUtc, overview } = useEpisode(
    episodeId,
    episodeEntity
  ) as Episode;

  const {
    path,
    mediaInfo,
    size,
    languages,
    quality,
    qualityCutoffNotMet,
    customFormats,
    customFormatScore,
  } = useEpisodeFile(episodeFileId) || {};

  const handleDeleteEpisodeFile = useCallback(() => {
    dispatch(
      deleteEpisodeFile({
        id: episodeFileId,
        episodeEntity,
      })
    );
  }, [episodeFileId, episodeEntity, dispatch]);

  const handleEditPath = useCallback(() => {
    setNewPath(path || '');
    setEditingPath(true);
  }, [path]);

  const handleCancelEdit = useCallback(() => {
    setEditingPath(false);
    setNewPath('');
  }, []);

  const handleSavePath = useCallback(() => {
    if (newPath && newPath !== path && episodeFileId) {
      setIsSaving(true);
      dispatch(
        moveEpisodeFile({
          id: episodeFileId,
          newPath,
        })
      );
      // Reset form after a brief delay to allow Redux action to process
      setTimeout(() => {
        setEditingPath(false);
        setNewPath('');
        setIsSaving(false);
        // Refresh episode file data to get any updates from parsing
        dispatch(fetchEpisodeFile({ id: episodeFileId }));
      }, 500);
    }
  }, [newPath, path, episodeFileId, dispatch]);

  const handlePathChange = useCallback((change: InputChanged<unknown>) => {
    setNewPath(String(change.value));
  }, []);

  useEffect(() => {
    if (episodeFileId && !path) {
      dispatch(fetchEpisodeFile({ id: episodeFileId }));
    }
  }, [episodeFileId, path, dispatch]);

  const hasOverview = !!overview;

  return (
    <div>
      <div>
        <span className={styles.infoTitle}>{translate('Airs')}</span>

        <EpisodeAiring airDateUtc={airDateUtc} network={network} />
      </div>

      <div>
        <span className={styles.infoTitle}>{translate('QualityProfile')}</span>

        <Label kind={kinds.PRIMARY} size={sizes.MEDIUM}>
          <QualityProfileNameConnector qualityProfileId={qualityProfileId} />
        </Label>
      </div>

      <div className={styles.overview}>
        {hasOverview ? overview : translate('NoEpisodeOverview')}
      </div>

      {path ? (
        <>
          <Table columns={COLUMNS}>
            <TableBody>
              <EpisodeFileRow
                path={path}
                size={size!}
                languages={languages!}
                quality={quality!}
                qualityCutoffNotMet={qualityCutoffNotMet!}
                customFormats={customFormats!}
                customFormatScore={customFormatScore!}
                mediaInfo={mediaInfo!}
                columns={COLUMNS}
                onDeleteEpisodeFile={handleDeleteEpisodeFile}
              />
            </TableBody>
          </Table>

          <div className={styles.filePathEditor}>
            <span className={styles.infoTitle}>{translate('EditFilePath')}</span>
            <div className={styles.pathInputContainer}>
              <TextInput
                value={editingPath ? newPath : path}
                onChange={handlePathChange}
                readOnly={!editingPath}
                className={styles.pathInput}
                name="episodeFilePath"
              />
              <div className={styles.buttonGroup}>
                {!editingPath ? (
                  <Button
                    kind={kinds.PRIMARY}
                    onPress={handleEditPath}
                    isDisabled={isSaving}
                  >
                    {translate('Edit')}
                  </Button>
                ) : (
                  <>
                    <Button
                      kind={kinds.SUCCESS}
                      onPress={handleSavePath}
                      isDisabled={isSaving || newPath === path}
                    >
                      {translate('Save')}
                    </Button>
                    <Button
                      kind={kinds.DANGER}
                      onPress={handleCancelEdit}
                      isDisabled={isSaving}
                    >
                      {translate('Cancel')}
                    </Button>
                  </>
                )}
              </div>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}

export default EpisodeSummary;
